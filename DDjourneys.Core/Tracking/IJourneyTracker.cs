using DDjourneys.Core.Models;

namespace DDjourneys.Core.Tracking;

/// <summary>
/// Follows journeys with a provider-side monitoring service and keeps the
/// list of followed journeys ("watchlist") in sync with that service.
/// </summary>
public interface IJourneyTracker
{
	/// <summary>False where no tracking service can be used.</summary>
	bool IsAvailable { get; }

	/// <summary>Broadcast of tracking events. Every enumeration is an independent subscription.</summary>
	IAsyncEnumerable<JourneyTrackingEvent> Events { get; }

	/// <summary>Immutable snapshot of all followed journeys, never null.</summary>
	IReadOnlyList<WatchedJourney> Watched { get; }

	/// <summary>Raised (on an arbitrary thread) whenever <see cref="Watched"/> was replaced.</summary>
	event EventHandler? WatchedChanged;

	/// <summary>False if the user (or the system) blocks the notifications that carry live updates.</summary>
	Task<bool> CanNotifyAsync(CancellationToken cancellationToken = default);

	/// <summary>Finds the followed entry that belongs to <paramref name="journey"/>, if any.</summary>
	WatchedJourney? Find(Journey journey);

	/// <summary>Starts following; an already followed journey is re-activated instead of duplicated.</summary>
	Task<WatchedJourney> FollowAsync(Journey journey, CancellationToken cancellationToken = default);

	/// <summary>Reloads the watchlist from the provider.</summary>
	Task RefreshAsync(CancellationToken cancellationToken = default);

	/// <summary>Pauses (false) or resumes (true) monitoring of one followed journey.</summary>
	Task SetActiveAsync(string planId, bool active, CancellationToken cancellationToken = default);

	Task SetOptionsAsync(string planId, WatchOptions options, CancellationToken cancellationToken = default);

	/// <summary>Stops following one journey and removes it from the provider.</summary>
	Task DeleteAsync(string planId, CancellationToken cancellationToken = default);

	/// <summary>Stops following everything.</summary>
	Task DeleteAllAsync(CancellationToken cancellationToken = default);

	// ----- Live presentation (defaults keep simple implementations short) -----

	/// <summary>True when the platform shows live state outside the app (e.g. a live notification).</summary>
	bool HasLiveSurface => false;

	/// <summary>The plan the live presentation currently follows; null when none is shown.</summary>
	string? LivePlanId => null;

	/// <summary>The plan the user chose for the live presentation; null means automatic.</summary>
	string? PreferredLivePlanId => null;

	/// <summary>
	/// Chooses the plan for the live presentation (null: automatic, the next journey under way).
	/// The choice applies while that plan is under way or about to start, and is kept across restarts.
	/// <see cref="WatchedChanged"/> is raised when the live plan changes.
	/// </summary>
	Task SetPreferredLivePlanAsync(string? planId, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	/// <summary>
	/// Hides the notice a followed journey currently shows (the user swiped it away). A newer notice shows again.
	/// </summary>
	Task DismissNoticeAsync(string planId, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	/// <summary>The full course of a followed journey with every stop, or null while it is not known yet.</summary>
	TrackedTrip? GetTrip(string planId) => null;
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
