namespace DDjourneys.Core.Tracking;

public enum TrackedStopState
{
	/// <summary>Left behind.</summary>
	Passed,

	/// <summary>The most recent stop of the segment under way (the vehicle is at or just after it).</summary>
	Current,

	/// <summary>The next stop of the segment under way.</summary>
	Next,

	/// <summary>Still ahead.</summary>
	Upcoming
}

/// <summary>One stop of a followed journey with planned and real-time time.</summary>
public sealed record TrackedStop(
	string Name,
	DateTimeOffset? Scheduled,
	DateTimeOffset? Realtime,
	TrackedStopState State,
	string? Platform = null,
	bool PlatformIsTrack = false)
{
	public DateTimeOffset? Effective => Realtime ?? Scheduled;

	/// <summary>Real-time minus planned; null without real-time data.</summary>
	public TimeSpan? Delay =>
		Realtime is { } actual && Scheduled is { } planned
			? actual - planned
			: null;
}

/// <summary>A ride or a walk of a followed journey. A walk has only its two ends as stops.</summary>
public sealed record TrackedSegment(
	bool IsWalk,
	string? Line,
	string? Direction,
	IReadOnlyList<TrackedStop> Stops,
	bool IsPassed,
	bool IsCurrent,
	TimeSpan? Duration = null)
{
	public TrackedStop? From => Stops.Count > 0 ? Stops[0] : null;

	public TrackedStop? To => Stops.Count > 0 ? Stops[^1] : null;

	/// <summary>
	/// A walk that goes nowhere: it starts and ends at the same stop and the platforms are not known to differ.
	/// Such a "change" is just waiting for the next vehicle and is not worth a row of its own.
	/// </summary>
	public bool IsInPlaceChange =>
		IsWalk
		&& From is { } from
		&& To is { } to
		&& from.Name.Trim().Length > 0
		&& string.Equals(from.Name.Trim(), to.Name.Trim(), StringComparison.OrdinalIgnoreCase)
		&& !PlatformsDiffer(from, to);

	private static bool PlatformsDiffer(TrackedStop a, TrackedStop b) =>
		!string.IsNullOrWhiteSpace(a.Platform)
		&& !string.IsNullOrWhiteSpace(b.Platform)
		&& !string.Equals(a.Platform.Trim(), b.Platform.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The full course of a followed journey at one moment: every segment with every stop. Rebuilt
/// from the cached real-time data on each request, so it moves with the clock between polls.
/// </summary>
public sealed record TrackedTrip(
	string PlanId,
	IReadOnlyList<TrackedSegment> Segments,
	DateTimeOffset At)
{
	public bool IsEmpty => Segments.Count == 0;
}

/// <summary>
/// Which followed journey owns the single live presentation. The user's choice wins while that
/// journey is eligible (under way, or about to start); otherwise the automatic choice applies.
/// </summary>
public static class LivePlanSelector
{
	/// <param name="preferred">The user's choice; null for automatic.</param>
	/// <param name="eligible">Eligible plans in automatic priority order (first = automatic choice).</param>
	public static string? Choose(string? preferred, IReadOnlyList<string> eligible)
	{
		ArgumentNullException.ThrowIfNull(eligible);

		if (preferred is not null
			&& eligible.Contains(preferred, StringComparer.Ordinal))
		{
			return preferred;
		}

		return eligible.Count > 0 ? eligible[0] : null;
	}
}
