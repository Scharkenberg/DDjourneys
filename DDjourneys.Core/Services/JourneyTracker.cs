namespace DDjourneys.Core.Services;

using DDjourneys.Core.Models;

public sealed class JourneyTracker
{
	private TrackedJourney? _trackedJourney;


	public TrackedJourney? Current =>
		_trackedJourney;


	public void Start(Journey journey)
	{
		_trackedJourney = new TrackedJourney
		{
			Journey = journey,
			CurrentLegIndex = 0,
			IsActive = true
		};
	}


	public void Stop()
	{
		_trackedJourney = null;
	}

	private static JourneyTrackingState DetermineState(
	JourneyLeg leg,
	DateTimeOffset now)
	{
		if (leg.Mode == TransitMode.Walk)
		{
			if (leg.EffectiveArrival <= now)
			{
				return JourneyTrackingState.TransferWaiting;
			}

			return JourneyTrackingState.WalkingToStart;
		}


		if (leg.EffectiveDeparture <= now
			&& leg.EffectiveArrival > now)
		{
			return JourneyTrackingState.OnVehicle;
		}


		if (leg.EffectiveArrival <= now)
		{
			return JourneyTrackingState.TransferWaiting;
		}


		return JourneyTrackingState.NotStarted;
	}

	public void UpdateState(DateTimeOffset now)
	{
		if (_trackedJourney is null)
		{
			return;
		}


		var leg = _trackedJourney.CurrentLeg;

		if (leg is null)
		{
			_trackedJourney.State =
				JourneyTrackingState.Completed;

			return;
		}


		_trackedJourney.State =
			DetermineState(leg, now);
	}
}