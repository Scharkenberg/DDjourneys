using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Maps VVO provider DTOs into DDjourneys domain models.
/// </summary>
public static class VvoJourneyMapper
{
	public static IReadOnlyList<Journey> Map(
		VvoTripResponse response)
	{
		ArgumentNullException.ThrowIfNull(response);

		return response.Routes
			.Select(MapJourney)
			.ToArray();
	}


	private static Journey MapJourney(VvoRoute route)
	{
		VvoDebug.DumpRoute(route);
		var legs = new List<JourneyLeg>();
		var transfers = new List<JourneyTransfer>();

		var mappedParts =
			new List<(VvoPartialRoute Route, JourneyLeg? Leg)>();


		// First pass: map all movement legs.
		foreach (var partialRoute in route.PartialRoutes)
		{
			System.Diagnostics.Debug.WriteLine(
	$"""
	[VVO PARTIAL]
	Type={partialRoute.Mot?.Type}
	Name={partialRoute.Mot?.Name}
	Duration={partialRoute.Duration}
	Stops={partialRoute.RegularStops.Count}
	"""
);
			if (IsTransfer(partialRoute))
			{
				mappedParts.Add(
					(partialRoute, null));
			}
			else
			{
				var leg =
					MapLeg(
						partialRoute,
						legs.LastOrDefault()?.To,
						GetFirstStation(partialRoute));

				System.Diagnostics.Debug.WriteLine(
	$"""
	[VVO LEG]
	Index: {legs.Count}
	Type: {partialRoute.Mot?.Type}
	Name: {partialRoute.Mot?.Name}
	Duration: {partialRoute.Duration} min
	FirstStop:
	  {partialRoute.RegularStops.FirstOrDefault()?.Name}
	  {partialRoute.RegularStops.FirstOrDefault()?.DepartureTime}
	LastStop:
	  {partialRoute.RegularStops.LastOrDefault()?.Name}
	  {partialRoute.RegularStops.LastOrDefault()?.ArrivalTime}
	""");
				legs.Add(leg);

				mappedParts.Add(
					(partialRoute, leg));
			}
		}


		// Second pass: map transfers with surrounding leg context.
		for (int i = 0; i < mappedParts.Count; i++)
		{
			var part = mappedParts[i];


			if (!IsTransfer(part.Route))
			{
				continue;
			}


			JourneyLeg? previousLeg = null;
			JourneyLeg? nextLeg = null;


			for (int previous = i - 1;
				previous >= 0;
				previous--)
			{
				if (mappedParts[previous].Leg is not null)
				{
					previousLeg =
						mappedParts[previous].Leg;

					break;
				}
			}


			for (int next = i + 1;
				next < mappedParts.Count;
				next++)
			{
				if (mappedParts[next].Leg is not null)
				{
					nextLeg =
						mappedParts[next].Leg;

					break;
				}
			}


			transfers.Add(
				MapTransfer(
					part.Route,
					previousLeg,
					nextLeg));
		}


		if (legs.Count == 0)
		{
			throw new InvalidOperationException(
				"VVO route contains no partial routes.");
		}


		return new Journey
		{
			From = legs[0].From,

			To = legs[^1].To,

			Legs = legs,

			Transfers = transfers,

			PlannedDuration =
				TimeSpan.FromMinutes(route.Duration)
		};
	}

	private static Station? GetFirstStation(
	VvoPartialRoute route)
	{
		return route.RegularStops
			.Select(MapStop)
			.FirstOrDefault()
			?.Station;
	}

	private static bool IsTransfer(VvoPartialRoute route)
	{
		if (string.Equals(
			route.Mot?.Type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}


		// VVO represents walking connections as Footpath.
		// If it has no transport stops, it is a transfer, not a journey leg.
		if (string.Equals(
			route.Mot?.Type,
			"Footpath",
			StringComparison.OrdinalIgnoreCase)
			&& route.RegularStops.Count == 0)
		{
			return true;
		}


		return false;
	}

	private static JourneyTransfer MapTransfer(
		VvoPartialRoute route,
		JourneyLeg? previousLeg,
		JourneyLeg? nextLeg)
	{
		var arrivalStop =
			previousLeg?
				.Stops
				.LastOrDefault();


		var departureStop =
			nextLeg?
				.Stops
				.FirstOrDefault();


		if (arrivalStop is null
			&& departureStop is null)
		{
			throw new InvalidOperationException(
				"VVO transfer has no identifiable location.");
		}

		DebugTransfer(route, departureStop, arrivalStop);

		var location =
			arrivalStop?.Station
			?? departureStop!.Station;


		return new JourneyTransfer
		{
			Location = location,


			Duration =
				TimeSpan.FromMinutes(route.Duration),


			Kind =
				DetermineTransferKind(
					route,
					arrivalStop,
					departureStop),


			IsGuaranteed =
				!route.ChangeoverEndangered,


			ArrivalPlatform =
				arrivalStop?.Platform,


			DeparturePlatform =
				departureStop?.Platform,


			Notices =
				route.Infos
		};
	}

	private static TransferKind DetermineTransferKind(
		VvoPartialRoute route,
		StopTime? arrival,
		StopTime? departure)
	{
		if (route.Mot?.Type == "Footpath")
		{
			return TransferKind.Walk;
		}


		if (arrival?.Station.Id != null
			&& departure?.Station.Id != null
			&& !string.Equals(
				arrival.Station.Id,
				departure.Station.Id,
				StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.Walk;
		}


		if (!string.Equals(
				arrival?.Platform,
				departure?.Platform,
				StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.PlatformChange;
		}


		return TransferKind.SameStop;
	}

	private static JourneyLeg MapLeg(
	VvoPartialRoute route,
	Station? previousDestination,
	Station? nextOrigin)
	{
		var stops = route.RegularStops
			.Select(MapStop)
			.ToArray();


		Station from;
		Station to;


		if (stops.Length > 0)
		{
			from = stops[0].Station;
			to = stops[^1].Station;
		}
		else
		{
			from = previousDestination ?? nextOrigin ?? throw new InvalidOperationException("Cannot determine location of VVO non-stop leg.");

			to = nextOrigin ?? previousDestination;
		}


		StopTime? firstStop =
			stops.FirstOrDefault();

		StopTime? lastStop =
			stops.LastOrDefault();


		return new JourneyLeg
		{
			Mode = MapMode(route.Mot),

			From = from,

			To = to,

			Stops = stops,

			Line = MapLine(route),

			Vehicle = MapVehicle(route),

			ScheduledDeparture = firstStop?.ScheduledDeparture,

			RealtimeDeparture =	firstStop?.RealtimeDeparture,

			ScheduledArrival = lastStop?.ScheduledArrival,

			RealtimeArrival = lastStop?.RealtimeArrival,

			DeparturePlatform =	firstStop?.Platform,

			ArrivalPlatform = lastStop?.Platform,

			IsCancelled = route.TripCancelled,

			Notices = route.Infos
		};
	}


	private static StopTime MapStop(
		VvoStop stop)
	{
		return new StopTime
		{
			Station = new Station
			{
				Id = stop.DataId ?? string.Empty,

				Name = stop.Name ?? string.Empty,

				Place = stop.Place
			},

			ScheduledArrival =
				stop.ArrivalTime,

			RealtimeArrival =
				stop.ArrivalRealTime,

			ScheduledDeparture =
				stop.DepartureTime,

			RealtimeDeparture =
				stop.DepartureRealTime,

			Platform =
				stop.Platform?.Name,

			IsCancelled =
				stop.ArrivalState == "Cancelled"
				||
				stop.DepartureState == "Cancelled"
		};
	}


	private static TransitLine? MapLine(
		VvoPartialRoute route)
	{
		if (route.Mot is null)
		{
			return null;
		}


		return new TransitLine
		{
			Name =
				route.Mot.Name
				?? string.Empty,

			Mode =
				MapMode(route.Mot),

			Operator =
				route.Mot.TransportationCompany
				?? route.Mot.Diva?.Network,

			Destination =
				route.Mot.Direction
		};
	}

	private static Vehicle? MapVehicle(
		VvoPartialRoute route)
	{
		if (route.Mot is null)
		{
			return null;
		}


		return new Vehicle
		{
			Id =
				route.Mot.StatelessId,

			Name = route.Mot.TrainNumber ?? route.Mot.Name,

			Operator =
				route.Mot.TransportationCompany
				?? route.Mot.Diva?.Network
		};
	}

	private static TransitMode MapMode(
		VvoMot? mot)
	{
		if (mot is null)
		{
			return TransitMode.Unknown;
		}


		string value =
			$"{mot.Type} {mot.Name}"
				.ToLowerInvariant();


		if (value.Contains("tram"))
		{
			return TransitMode.Tram;
		}

		if (value.Contains("bus"))
		{
			return TransitMode.Bus;
		}

		if (value.Contains("s-bahn")|| value.Contains("suburban") || mot.Type == "RapidTransit")
		{
			return TransitMode.SuburbanRail;
		}

		if (mot.Type == "Train")
		{
			return TransitMode.RegionalTrain;
		}

		if (value.Contains("ferry"))
		{
			return TransitMode.Ferry;
		}

		if (value.Contains("taxi"))
		{
			return TransitMode.Taxi;
		}

		return TransitMode.Unknown;
	}

	private static void DebugTransfer(
	VvoPartialRoute route,
	StopTime? firstStop,
	StopTime? lastStop)
	{
		System.Diagnostics.Debug.WriteLine(
			$"""
		[VVO TRANSFER]
		PartialRouteId: {route.PartialRouteId}
		Duration raw: {route.Duration} min
		Kind: {route.Mot?.Type} / {route.Mot?.Name}
		Stops: {route.RegularStops.Count}
		First:
		  Station: {firstStop?.Station.Name}
		  Id: {firstStop?.Station.Id}
		  Arrival: {firstStop?.ScheduledArrival:o}
		  Departure: {firstStop?.ScheduledDeparture:o}
		  Platform: {firstStop?.Platform}
		Last:
		  Station: {lastStop?.Station.Name}
		  Id: {lastStop?.Station.Id}
		  Arrival: {lastStop?.ScheduledArrival:o}
		  Departure: {lastStop?.ScheduledDeparture:o}
		  Platform: {lastStop?.Platform}
		Infos:
		  {string.Join(" | ", route.Infos)}
		""");
	}

}