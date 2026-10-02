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

	public static IReadOnlyList<int> LeadChoices { get; } = [1, 3, 5, 10, 15, 30];
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
	bool IsPeriodic);
