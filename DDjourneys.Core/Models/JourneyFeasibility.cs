namespace DDjourneys.Core.Models;

public enum JourneyBlockKind
{
	/// <summary>A ride of the journey does not run.</summary>
	RideCancelled,

	/// <summary>The vehicle runs but does not serve the stop where one gets on.</summary>
	BoardingNotServed,

	/// <summary>The vehicle runs but does not serve the stop where one gets off.</summary>
	AlightingNotServed,

	/// <summary>By the real-time data, the next ride leaves before one can get there.</summary>
	ConnectionBroken
}

/// <summary>The first thing that makes a journey impossible.</summary>
/// <param name="LegIndex">Index into <see cref="Journey.Legs"/> of the affected ride (for a broken connection: the ride that is missed).</param>
/// <param name="Stop">The affected stop, when one applies.</param>
/// <param name="Line">Line name of the affected ride, when known.</param>
public sealed record JourneyBlock(JourneyBlockKind Kind, int LegIndex, Station? Stop, string? Line);

/// <summary>
/// Decides whether a journey can still be travelled. The provider marks single facts (a trip cancelled,
/// a stop's arrival or departure cancelled); this combines them the way a passenger experiences them:
/// a tram that runs but skips the stop where you wanted to get on makes the journey just as impossible
/// as a cancelled tram.
/// </summary>
public static class JourneyFeasibility
{
	public static JourneyBlock? Assess(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		JourneyLeg? previousRide = null;
		int previousRideIndex = -1;

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			JourneyLeg leg = journey.Legs[i];

			if (!leg.IsRide)
			{
				continue;
			}

			string? line = leg.Line?.Name;

			if (leg.IsCancelled)
			{
				return new JourneyBlock(JourneyBlockKind.RideCancelled, i, leg.From, line);
			}

			if (leg.Stops.Count > 0 && leg.Stops[0].CannotBoard)
			{
				return new JourneyBlock(JourneyBlockKind.BoardingNotServed, i, leg.Stops[0].Station, line);
			}

			if (leg.Stops.Count > 1 && leg.Stops[^1].CannotAlight)
			{
				return new JourneyBlock(JourneyBlockKind.AlightingNotServed, i, leg.Stops[^1].Station, line);
			}

			// Only real-time data can break a planned connection; the timetable itself is consistent.
			// Broken means certain: the next vehicle leaves before the previous one arrives. Footpaths are not
			// added (a change without a real footpath carries its whole wait as duration); a merely tight change
			// is the provider's "endangered" flag, not an impossibility.
			// An ensured connection (the next vehicle waits) is never broken by the clock: the provider's
			// departure time may still show the timetable while the vehicle is held back.
			if (previousRide is not null
				&& !IsEnsuredChange(journey, previousRideIndex, i)
				&& (previousRide.RealtimeArrival.HasValue || leg.RealtimeDeparture.HasValue)
				&& previousRide.EffectiveArrival is { } arrival
				&& leg.EffectiveDeparture is { } departure
				&& departure < arrival)
			{
				return new JourneyBlock(JourneyBlockKind.ConnectionBroken, i, leg.From, line);
			}

			previousRide = leg;
			previousRideIndex = i;
		}

		return null;
	}

	/// <summary>
	/// True when the provider ensures the change from the ride at <paramref name="fromLegIndex"/> to the ride at
	/// <paramref name="toLegIndex"/> (the connecting vehicle waits). Legs in between (a footpath) belong to the change.
	/// </summary>
	public static bool IsEnsuredChange(Journey journey, int fromLegIndex, int toLegIndex)
	{
		ArgumentNullException.ThrowIfNull(journey);

		foreach (JourneyTransfer transfer in journey.Transfers)
		{
			if (transfer.IsEnsured
				&& transfer.PreviousLegIndex is { } previous
				&& transfer.NextLegIndex is { } next
				&& previous >= fromLegIndex
				&& next <= toLegIndex
				&& previous < toLegIndex
				&& next > fromLegIndex)
			{
				return true;
			}
		}

		return false;
	}

	// ----- Walk pace (roadmap #6, B5: "tight at your pace") -----

	/// <summary>
	/// How much longer (or shorter) a walk takes at this pace than at <see cref="WalkingPace.Normal"/> - the
	/// pace the providers' own footpath quotes assume (the VVO is sent the chosen pace, so its quotes already
	/// carry it; the tracking service's walks do not). Each step away from Normal changes the time by a
	/// quarter: this is the app's own convention, not a published provider factor.
	/// </summary>
	public static double PaceFactor(WalkingPace pace) =>
			pace switch
			{
				WalkingPace.VerySlow => 1.5625,
				WalkingPace.Slow => 1.25,
				WalkingPace.Normal => 1.0,
				WalkingPace.Fast => 0.8,
				WalkingPace.VeryFast => 0.64,
				_ => 1.0
			};

	/// <summary>The quoted walk duration at the user's own pace (rounded up to the second).</summary>
	public static TimeSpan WalkAtPace(TimeSpan quoted, WalkingPace pace) =>
			TimeSpan.FromSeconds(Math.Ceiling(quoted.TotalSeconds * PaceFactor(pace)));

	/// <summary>
	/// True when the change works only because the passenger walks faster than their own setting: the quoted
	/// walk still fits the window, but at the user's pace it does not. A pace at or above Normal is never
	/// tight at the user's pace (a faster pace only makes the change easier); a difference of less than half a
	/// minute is rounding, not tightness.
	/// </summary>
	/// <param name="quotedWalk">The walk duration the data quotes for the change (see <c>TimelineBuilder</c>).</param>
	/// <param name="wait">The time left to change once the quoted walk is subtracted (the change's own wait).</param>
	public static bool IsTightAtPace(TimeSpan? quotedWalk, TimeSpan wait, WalkingPace pace)
	{
		if (quotedWalk is not { } walk || walk <= TimeSpan.Zero || PaceFactor(pace) <= 1.0)
		{
			return false;
		}

		// The window the walk may use is the wait plus the quoted walk itself; at the user's pace the walk needs more.
		return WalkAtPace(walk, pace) > wait + walk + TimeSpan.FromSeconds(30);
	}
}
