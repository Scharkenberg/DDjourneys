namespace DDjourneys.Core.Models;

public sealed class TrackedJourney
{
	public required Journey Journey { get; init; }


	/// <summary>
	/// Current leg index being monitored.
	/// </summary>
	public int CurrentLegIndex { get; set; }


	/// <summary>
	/// Whether automatic monitoring is active.
	/// </summary>
	public bool IsActive { get; set; }

	/// <summary>
	/// Tracking state of the journey. Default is NotStarted.
	/// </summary>
	public JourneyTrackingState State { get; set; }
		= JourneyTrackingState.NotStarted;


	/// <summary>
	/// Last successful update time.
	/// </summary>
	public DateTimeOffset? LastUpdate { get; set; }


	/// <summary>
	/// Current known delay.
	///
	/// Positive = late.
	/// Negative = early.
	/// </summary>
	public TimeSpan? CurrentDelay { get; set; }


	public JourneyLeg? CurrentLeg =>
		CurrentLegIndex >= 0
		&& CurrentLegIndex < Journey.Legs.Count
			? Journey.Legs[CurrentLegIndex]
			: null;
}