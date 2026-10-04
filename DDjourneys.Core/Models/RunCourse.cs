namespace DDjourneys.Core.Models;

/// <summary>
/// Works on the stops a provider returns for a run. The VVO answer is a sliding window over chained journeys
/// of the same vehicle, and its "Previous/Current/Next" is relative to the queried stop, not to the vehicle.
/// </summary>
public static class RunCourse
{
	/// <summary>
	/// The stops of the one journey around the queried stop (the entry marked Current whose scheduled time is
	/// <paramref name="scheduledAtStop"/>). A journey ends where the vehicle turns round at a terminus: the same
	/// stop twice in a row, a stop between two visits of the same stop, or a time that jumps back.
	/// Returns the list unchanged when the anchor cannot be found.
	/// </summary>
	public static IReadOnlyList<RunStop> Isolate(
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
			return stops;
		}

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
			&& !(Id(start - 1) == Id(start) || Turnaround(start) || TimeJumpsBack(start - 1, start)))
		{
			start--;
		}

		int end = Math.Max(anchor, start + 1);

		while (end < stops.Count - 1
			&& !(Id(end) == Id(end + 1) || Turnaround(end) || TimeJumpsBack(end, end + 1)))
		{
			end++;
		}

		end = Math.Min(end, stops.Count - 1);

		return end - start + 1 >= 2
			? [.. stops.Skip(start).Take(end - start + 1)]
			: stops;
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
