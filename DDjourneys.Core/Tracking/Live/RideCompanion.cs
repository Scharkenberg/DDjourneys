namespace DDjourneys.Core.Tracking.Live;

/// <summary>The get-off alarm of a followed ride (roadmap #9): how long before the alighting stop it rings.</summary>
public static class GetOffAlarm
{
	/// <summary>The lead times the app offers, minutes; 0 means the alarm is off.</summary>
	public static IReadOnlyList<int> LeadChoices { get; } = [0, 1, 2, 3];

	/// <summary>The stored lead brought back into the offered range (a preference can hold anything).</summary>
	public static int Lead(int minutes) =>
		Math.Clamp(minutes, LeadChoices[0], LeadChoices[^1]);

	/// <summary>The alarm's own default (what the notification alone did before the alarm existed: about a minute).</summary>
	public const int DefaultLead = 1;

	/// <summary>
	/// Whether the alarm is due at <paramref name="now"/>: the ride ends within the lead time (or has just
	/// passed it - a late alert still tells the passenger to get off). Once per ride, whatever calls this.
	/// </summary>
	public static bool Due(DateTimeOffset? alightingAt, int leadMinutes, DateTimeOffset now)
	{
		if (leadMinutes <= 0 || alightingAt is not { } at)
		{
			return false;
		}

		return at - now <= TimeSpan.FromMinutes(Lead(leadMinutes));
	}
}

/// <summary>What is left of the ride that is under way: the stops until the get-off stop, and the time until it.</summary>
public sealed record RideCountdown(int Stops, TimeSpan? ExitIn);

/// <summary>
/// The ride companion's countdown (roadmap #9): "N stops to go · exit in ~M min". Honest about where the
/// numbers come from - the minutes are the run's own schedule when it carries a time for the get-off stop;
/// when it does not (but times other stops), the remaining stops times the ride's typical spacing between
/// stops (the median of the timed gaps); when the ride carries no times at all, only the stop count is
/// claimed and no time is invented.
/// </summary>
public static class RideCountdowns
{
	/// <summary>One stop of the ride that is under way, with the time the run reaches it (real time preferred).</summary>
	public readonly record struct StopVisit(string Name, DateTimeOffset? At);

	/// <param name="stops">The ride's stops in travel order, ending with the get-off stop (the caller appends it when the provider's own list lacks it).</param>
	/// <param name="now">The clock the countdown is against.</param>
	/// <returns>The countdown, or null when there is nothing to count (no stops, or none the ride still has ahead).</returns>
	public static RideCountdown? Of(IReadOnlyList<StopVisit> stops, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(stops);

		if (stops.Count == 0)
		{
			return null;
		}

		bool anyTime = stops.Any(stop => stop.At is not null);

		// Without a single time the ride cannot say which stops are behind; counting them all is the honest ceiling.
		if (!anyTime)
		{
			return new RideCountdown(stops.Count, null);
		}

		int remaining =
			stops.Count(stop => stop.At is { } at && at > now);

		// The get-off stop always counts, also when its time has just passed (the ride is still the current one).
		int ahead = Math.Max(1, remaining);

		TimeSpan? exit =
			stops[^1].At is { } last
				? last - now
				: ahead * MedianGap(stops);

		return new RideCountdown(ahead, exit);
	}

	/// <summary>The ride's own rhythm: the median of the gaps between the stops that carry times.</summary>
	private static TimeSpan MedianGap(IReadOnlyList<StopVisit> stops)
	{
		var gaps = new List<double>();

		for (int i = 0; i + 1 < stops.Count; i++)
		{
			if (stops[i].At is { } from
				&& stops[i + 1].At is { } to
				&& to > from)
			{
				gaps.Add((to - from).TotalSeconds);
			}
		}

		if (gaps.Count == 0)
		{
			return TimeSpan.Zero;
		}

		gaps.Sort();

		return TimeSpan.FromSeconds(gaps[gaps.Count / 2]);
	}
}
