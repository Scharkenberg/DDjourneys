namespace DDjourneys.Core.Tracking;

public enum WatchStatus
{
	Planned,
	Active,
	Recent,
	Deactivated
}

/// <summary>Which alerts a followed journey raises.</summary>
public sealed record WatchOptions(
	bool StartAlert = true,
	int StartLeadMinutes = 5,
	bool ChangeAlert = true,
	bool ProblemAlert = true)
{
	public static WatchOptions Default { get; } = new();

	/// <summary>The lead times the reference client offers (minutes).</summary>
	public static IReadOnlyList<int> LeadChoices { get; } = [3, 5, 10, 15, 20, 30, 45, 60];
}

/// <summary>One followed journey as shown on the overview page. Display values only.</summary>
public sealed record WatchedJourney(
	string PlanId,
	string? JourneyKey,
	string Origin,
	string Destination,
	DateTimeOffset? Departure,
	DateTimeOffset? Arrival,
	IReadOnlyList<string> Lines,
	WatchStatus Status,
	TrackingPhase Phase,
	double Progress,
	string? CurrentLine,
	string? NextStop,
	DateTimeOffset? NextStopTime,
	string? LatestNotice,
	WatchOptions Options,
	bool IsPeriodic,
	IReadOnlyList<bool>? EnsuredChanges = null,
	IReadOnlyList<WatchedNotice>? Notices = null);

/// <summary>One message of a followed journey. The reference client lists all of them, newest first.</summary>
public sealed record WatchedNotice(
	DateTimeOffset? Time,
	string Text,
	bool IsProblem);
