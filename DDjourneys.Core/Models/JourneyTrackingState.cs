namespace DDjourneys.Core.Models;

public enum JourneyTrackingState
{
	NotStarted,

	WalkingToStart,

	OnVehicle,

	TransferWaiting,

	WalkingToDestination,

	Completed,

	Cancelled
}