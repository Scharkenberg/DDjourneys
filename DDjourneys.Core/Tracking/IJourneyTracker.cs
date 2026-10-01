using DDjourneys.Core.Models;

namespace DDjourneys.Core.Tracking;

public interface IJourneyTracker
{
	bool IsAvailable { get; }
	IAsyncEnumerable<JourneyTrackingEvent> Events { get; }
	Task StartAsync(Journey journey, CancellationToken cancellationToken = default);
	Task StopAsync(CancellationToken cancellationToken = default);
}

public enum TrackingPhase
{
	Planned,
	InProgress,
	AtInterchange,
	AtRisk,
	Cancelled,
	Arrived,
	Paused
}

public sealed record LiveJourneyState(
	string JourneyId,
	TrackingPhase Phase,
	int CurrentLegIndex,
	int LegCount,
	DateTimeOffset? PlannedArrival,
	DateTimeOffset? EstimatedArrival,
	string? Message,
	DateTimeOffset UpdatedAt);

public enum JourneyTrackingEventKind
{
	Started,
	Updated,
	RiskChanged,
	Cancelled,
	Arrived
}

public sealed record JourneyTrackingEvent(
	JourneyTrackingEventKind Kind,
	LiveJourneyState State);
