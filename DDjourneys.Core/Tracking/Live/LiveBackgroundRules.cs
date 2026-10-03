namespace DDjourneys.Core.Tracking.Live;

/// <summary>
/// When a platform whose app process is the monitor (no foreground service) must keep that process
/// alive without a window: while the live presentation shows a journey that is being monitored.
/// </summary>
public static class LiveBackgroundRules
{
	public static bool KeepsProcessAlive(LiveJourneyContent? content) =>
		content is { Ongoing: true }
		&& content.Phase is not (TrackingPhase.Paused or TrackingPhase.Arrived or TrackingPhase.Cancelled);
}
