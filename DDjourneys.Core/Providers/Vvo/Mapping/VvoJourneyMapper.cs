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


	private static Journey MapJourney(
	VvoRoute route)
	{
		var legs = new List<JourneyLeg>();
		var transfers = new List<JourneyTransfer>();

		foreach (var partialRoute in route.PartialRoutes)
		{
			if (IsTransfer(partialRoute))
			{
				transfers.Add(
					MapTransfer(partialRoute));
			}
			else
			{
				legs.Add(
					MapLeg(
						partialRoute,
						legs.LastOrDefault()?.To,
						GetFirstStation(partialRoute)));
			}
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
			Transfers = transfers
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

	private static bool IsTransfer(
	VvoPartialRoute route)
	{
		return string.Equals(
			route.Mot?.Type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase);
	}

	private static JourneyTransfer MapTransfer(
	VvoPartialRoute route)
	{
		var stop =
			route.RegularStops
				.Select(MapStop)
				.FirstOrDefault();


		return new JourneyTransfer
		{
			Location =
				stop?.Station
				?? throw new InvalidOperationException(
					"VVO transfer has no location."),

			Duration =
				TimeSpan.FromMinutes(route.Duration),

			IsGuaranteed = true,

			Notices =
				route.Infos
		};
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

			ScheduledDeparture =
				firstStop?.ScheduledDeparture
				?? default,

			RealtimeDeparture =
				firstStop?.RealtimeDeparture,

			ScheduledArrival =
				lastStop?.ScheduledArrival
				?? default,

			RealtimeArrival =
				lastStop?.RealtimeArrival,

			DeparturePlatform =
				firstStop?.Platform,

			ArrivalPlatform =
				lastStop?.Platform,

			IsCancelled =
				route.TripCancelled,

			Notices =
				route.Infos
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
				route.Mot.Diva?.Number
				?? route.Mot.Name
				?? string.Empty,

			Mode =
				MapMode(route.Mot),

			Operator =
				route.Mot.Diva?.Network,

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
			Name = route.Mot.Name,

			Operator = route.Mot.Diva?.Network
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

		if (value.Contains("s-bahn")
			|| value.Contains("suburban"))
		{
			return TransitMode.SuburbanRail;
		}

		if (value.Contains("regional")
			|| value.Contains("train"))
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
}