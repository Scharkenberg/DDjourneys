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
}
