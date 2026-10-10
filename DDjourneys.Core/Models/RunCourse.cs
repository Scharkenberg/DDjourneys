namespace DDjourneys.Core.Models;

/// <summary>
/// Works on the stops a provider returns for a run. The VVO answer is a sliding window over chained journeys
/// of the same vehicle, and its "Previous/Current/Next" is relative to the queried stop, not to the vehicle.
/// </summary>
public static class RunCourse
{
	/// <summary>A wait between two consecutive stops that reaches this, and four times the ride's own
	/// median gap, is the seam between two chained journeys: the vehicle turns at a terminus, deadheads
	/// or lays over at a depot.</summary>
	private static readonly TimeSpan MinSeamWait = TimeSpan.FromMinutes(4);

	private static readonly int SeamMultiple = 4;

	/// <summary>
	/// The stops of the one journey around the queried stop (the entry marked Current whose scheduled time is
	/// <paramref name="scheduledAtStop"/>). A journey ends where the vehicle turns round at a terminus: the same
	/// stop twice in a row, a stop between two visits of the same stop, a time that jumps back, or a wait far
	/// beyond the rhythm of the ride (a trip that leaves its line for the depot instead of the usual terminus
	/// carries no stop pattern at its seam, only the depot layover before the vehicle's next trip).
	/// Returns the list unchanged when the anchor cannot be found.
	/// </summary>
	public static IReadOnlyList<RunStop> Isolate(
		IReadOnlyList<RunStop> stops,
		DateTimeOffset? scheduledAtStop)
	{
		ArgumentNullException.ThrowIfNull(stops);

		return Locate(stops, scheduledAtStop) is { } found
			? [.. stops.Skip(found.Start).Take(found.End - found.Start + 1)]
			: stops;
	}

	/// <summary>
	/// The span of the one journey around the queried stop that <see cref="Isolate"/> cuts to, as indices into
	/// the list: where the followed ride sits in the whole itinerary of the vehicle. Null when the anchor
	/// cannot be found or the span would hold less than two stops (the caller then keeps the whole list).
	/// </summary>
	public static (int Start, int End)? Locate(
		IReadOnlyList<RunStop> stops,
		DateTimeOffset? scheduledAtStop)
	{
		ArgumentNullException.ThrowIfNull(stops);

		int anchor = -1;

		for (int i = 0; i < stops.Count; i++)
		{
			if (stops[i].Position != RunPosition.Current)
			{
				continue;
			}

			if (anchor < 0)
			{
				anchor = i;
			}

			if (scheduledAtStop is { } wanted
				&& stops[i].Scheduled is { } scheduled
				&& Math.Abs((scheduled - wanted).TotalSeconds) < 90)
			{
				anchor = i;

				break;
			}
		}

		if (anchor < 0)
		{
			return null;
		}

		DateTimeOffset? TimeOf(int i) =>
			stops[i].Scheduled
			?? stops[i].Realtime;

		// The rhythm of the ride: the median of the gaps between the consecutive stops that have times.
		TimeSpan? TypicalGap()
		{
			var gaps = new List<double>();

			for (int i = 0; i + 1 < stops.Count; i++)
			{
				if (TimeOf(i) is { } from
					&& TimeOf(i + 1) is { } to
					&& to >= from)
				{
					gaps.Add(
						(to - from).TotalMinutes);
				}
			}

			if (gaps.Count == 0)
			{
				return null;
			}

			gaps.Sort();

			return TimeSpan.FromMinutes(
				gaps[gaps.Count / 2]);
		}

		TimeSpan? typical = TypicalGap();

		// The seam between two journeys: a wait the ride itself never makes. A hold within one journey, a
		// bridge without stops and a regional run between villages stay below this; the turn at a terminus
		// and the layover before the vehicle's next trip (out of the line and into the depot, or back onto
		// it) do not.
		bool LongWait(int earlier, int later) =>
			typical is { } rhythm
			&& TimeOf(earlier) is { } from
			&& TimeOf(later) is { } to
			&& to - from > (rhythm * SeamMultiple > MinSeamWait
				? rhythm * SeamMultiple
				: MinSeamWait);

		string Id(int i) => stops[i].Station.Id;

		bool Turnaround(int k) =>
			k > 0
			&& k < stops.Count - 1
			&& Id(k - 1) == Id(k + 1)
			&& Id(k - 1) != Id(k);

		bool TimeJumpsBack(int earlier, int later) =>
			stops[earlier].Effective is { } a
			&& stops[later].Effective is { } b
			&& b < a.AddMinutes(-2);

		int start = anchor;

		while (start > 0
			&& !(Id(start - 1) == Id(start)
				|| Turnaround(start)
				|| TimeJumpsBack(start - 1, start)
				|| LongWait(start - 1, start)))
		{
			start--;
		}

		int end = Math.Max(anchor, start + 1);

		while (end < stops.Count - 1
			&& !(Id(end) == Id(end + 1)
				|| Turnaround(end)
				|| TimeJumpsBack(end, end + 1)
				|| LongWait(end, end + 1)))
		{
			end++;
		}

		end = Math.Min(end, stops.Count - 1);

		return end - start + 1 >= 2
			? (start, end)
			: null;
	}

	/// <summary>
	/// Index of the stop the vehicle has last reached (its real-time or scheduled time is not after
	/// <paramref name="now"/>); -1 when it has not reached the first stop yet.
	/// </summary>
	public static int VehicleIndex(
		IReadOnlyList<RunStop> stops,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(stops);

		int index = -1;

		for (int i = 0; i < stops.Count; i++)
		{
			if (stops[i].Effective is { } time
				&& time <= now)
			{
				index = i;
			}
		}

		return index;
	}
}
