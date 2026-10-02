using System.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Maps VVO provider DTOs into DDjourneys domain models.
/// </summary>
public static class VvoJourneyMapper
{
	private static readonly ICoordinateTransformation Gk4ToWgs84 =
	CreateGk4ToWgs84Transformation();

	private static ICoordinateTransformation CreateGk4ToWgs84Transformation()
	{
		CoordinateSystemFactory coordinateSystemFactory =
			new();

		CoordinateTransformationFactory transformationFactory =
			new();

		const string gk4Wkt =
			"""
		PROJCS["DHDN / 3-degree Gauss-Kruger zone 4",
			GEOGCS["DHDN",
				DATUM["Deutsches_Hauptdreiecksnetz",
					SPHEROID["Bessel 1841",6377397.155,299.1528128],
					TOWGS84[598.1,73.7,418.2,0.202,0.045,-2.455,6.7]],
				PRIMEM["Greenwich",0],
				UNIT["degree",0.0174532925199433]],
			PROJECTION["Transverse_Mercator"],
			PARAMETER["latitude_of_origin",0],
			PARAMETER["central_meridian",12],
			PARAMETER["scale_factor",1],
			PARAMETER["false_easting",4500000],
			PARAMETER["false_northing",0],
			UNIT["metre",1]]
		""";

		CoordinateSystem source =
			coordinateSystemFactory.CreateFromWkt(
				gk4Wkt);

		CoordinateSystem target =
			GeographicCoordinateSystem.WGS84;

		return transformationFactory
			.CreateFromCoordinateSystems(
				source,
				target);
	}

	public static IReadOnlyList<Journey> Map(
		VvoTripResponse response)
	{
		ArgumentNullException.ThrowIfNull(response);

		return response.Routes
			.Select(
				route =>
					MapJourney(
						route,
						response.SessionId))
			.ToArray();
	}


	private static Journey MapJourney(
		VvoRoute route,
		string? sessionId)
	{
		VvoDebug.DumpRoute(route);

		var legs =
			new List<JourneyLeg>();

		var transfers =
			new List<JourneyTransfer>();

		var mappedParts =
			new List<
				(
					VvoPartialRoute Route,
					JourneyLeg? Leg,
					int? LegIndex
				)>();


		// First pass: map movement legs only.
		// Transfer/accessibility instructions remain out of Journey.Legs.
		foreach (VvoPartialRoute partialRoute
			in route.PartialRoutes)
		{
			System.Diagnostics.Debug.WriteLine(
				$"""
				[VVO PARTIAL]
				Type={partialRoute.Mot?.Type}
				Name={partialRoute.Mot?.Name}
				Duration={partialRoute.Duration}
				Stops={partialRoute.RegularStops.Count}
				""");

			if (IsTransfer(partialRoute))
			{
				mappedParts.Add(
					(partialRoute, null, null));

				continue;
			}

			var path = MapPath(route, partialRoute);

			JourneyLeg leg =
				MapLeg(
					partialRoute,
					legs.LastOrDefault()?.To,
					GetFirstStation(partialRoute),
					route.RouteCancelled,
					path);

			int legIndex =
				legs.Count;

			System.Diagnostics.Debug.WriteLine(
	$"""
	[VVO LEG]
	Index: {legIndex}
	Type: {partialRoute.Mot?.Type}
	Name: {partialRoute.Mot?.Name}
	Duration: {partialRoute.Duration} min
	Path points:
	  {path.Count}
	FirstStop:
	  {partialRoute.RegularStops.FirstOrDefault()?.Name}
	  {partialRoute.RegularStops.FirstOrDefault()?.DepartureTime}
	LastStop:
	  {partialRoute.RegularStops.LastOrDefault()?.Name}
	  {partialRoute.RegularStops.LastOrDefault()?.ArrivalTime}
	""");

			legs.Add(leg);

			mappedParts.Add(
				(partialRoute, leg, legIndex));
		}


		// Second pass: map transfer instructions with exact
		// surrounding leg indices.
		for (int i = 0;
			i < mappedParts.Count;
			i++)
		{
			var part =
				mappedParts[i];

			if (!IsTransfer(part.Route))
			{
				continue;
			}


			JourneyLeg? previousLeg = null;
			int? previousLegIndex = null;

			JourneyLeg? nextLeg = null;
			int? nextLegIndex = null;


			for (int previous = i - 1;
				previous >= 0;
				previous--)
			{
				if (mappedParts[previous].Leg is { } leg)
				{
					previousLeg = leg;
					previousLegIndex =
						mappedParts[previous].LegIndex;

					break;
				}
			}


			for (int next = i + 1;
				next < mappedParts.Count;
				next++)
			{
				if (mappedParts[next].Leg is { } leg)
				{
					nextLeg = leg;
					nextLegIndex =
						mappedParts[next].LegIndex;

					break;
				}
			}


			transfers.Add(
	MapTransfer(
		part.Route,
		previousLeg,
		previousLegIndex,
		nextLeg,
		nextLegIndex,
		MapPath(route, part.Route)));
		}


		if (legs.Count == 0)
		{
			throw new InvalidOperationException(
				"VVO route contains no movement legs.");
		}

		System.Diagnostics.Debug.WriteLine(
	$"""
	[VVO JOURNEY PATHS]
	Legs:
	{string.Join(
		Environment.NewLine,
		legs.Select(
			(l, i) =>
				$"  {i}: {l.Mode} {l.Line?.Name} Path={l.Path.Count}"))}

	Transfers:
	{string.Join(
		Environment.NewLine,
		transfers.Select(
			(t, i) =>
				$"  {i}: {t.Kind} Path={t.Path.Count}"))}
	""");

		return new Journey
		{
			From =
				legs[0].From,

			To =
				legs[^1].To,

			Legs =
				legs,

			Transfers =
				transfers,

			Id =
				route.RouteId.ToString(
					System.Globalization.CultureInfo.InvariantCulture),

			ProviderData =
				route,

			Context =
				string.IsNullOrWhiteSpace(sessionId)
					? null
					: sessionId,

			PlannedDuration =
				TimeSpan.FromMinutes(
					route.Duration)
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
		string? type =
			route.Mot?.Type;


		if (string.Equals(
			type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}


		// A Footpath without regular stops is a transfer instruction.
		// It is represented by JourneyTransfer rather than JourneyLeg.
		if (string.Equals(
			type,
			"Footpath",
			StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}


		// Mobility instructions describe accessibility movement
		// inside a transfer, not passenger transport.
		return type?.StartsWith(
			"Mobility",
			StringComparison.OrdinalIgnoreCase)
			== true;
	}


	private static JourneyTransfer MapTransfer(
		VvoPartialRoute route,
		JourneyLeg? previousLeg,
		int? previousLegIndex,
		JourneyLeg? nextLeg,
		int? nextLegIndex,
		IReadOnlyList<
			(double Latitude, double Longitude)> path)
	{
		StopTime? arrivalStop =
			previousLeg?
				.Stops
				.LastOrDefault();


		StopTime? departureStop =
			nextLeg?
				.Stops
				.FirstOrDefault();


		// A transfer partial route normally has no RegularStops.
		// Therefore its location must come from the surrounding
		// movement legs.
		Station? location =
			previousLeg?.To
			?? nextLeg?.From;


		if (location is null)
		{
			throw new InvalidOperationException(
				"VVO transfer has no identifiable location.");
		}


		DebugTransfer(
			route,
			departureStop
				?? nextLeg?
					.Stops
					.FirstOrDefault(),
			arrivalStop
				?? previousLeg?
					.Stops
					.LastOrDefault(),
			path.Count);


		return new JourneyTransfer
		{
			Location =
				location,

			Duration =
				TimeSpan.FromMinutes(
					Math.Max(
						0,
						route.Duration)),

			WaitingTime =
				DetermineWaitingTime(
					route,
					arrivalStop,
					departureStop),

			Kind =
				DetermineTransferKind(
					route,
					arrivalStop,
					departureStop),

			IsGuaranteed =
				!route.ChangeoverEndangered,

			ProviderData =
				route,

			ArrivalPlatform =
				arrivalStop?.Platform
				?? previousLeg?.ArrivalPlatform,

			ArrivalPlatformKind =
				arrivalStop?.Platform is not null
					? arrivalStop.PlatformKind
					: previousLeg?.ArrivalPlatformKind
						?? PlatformKind.Unknown,

			DeparturePlatform =
				departureStop?.Platform
				?? nextLeg?.DeparturePlatform,

			DeparturePlatformKind =
				departureStop?.Platform is not null
					? departureStop.PlatformKind
					: nextLeg?.DeparturePlatformKind
						?? PlatformKind.Unknown,

			PreviousLegIndex =
				previousLegIndex,

			NextLegIndex =
				nextLegIndex,

			Path =
				path,

			Notices =
				VvoNoticeParser.Parse(
					route.Infos)
		};
	}

	private static TimeSpan? DetermineWaitingTime(
		VvoPartialRoute route,
		StopTime? arrival,
		StopTime? departure)
	{
		if (!string.Equals(
			route.Mot?.Type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}


		if (arrival?.EffectiveArrival is { } arrives
			&& departure?.EffectiveDeparture is { } departs
			&& departs >= arrives)
		{
			return departs - arrives;
		}


		return null;
	}


	private static TransferKind DetermineTransferKind(
		VvoPartialRoute route,
		StopTime? arrival,
		StopTime? departure)
	{
		string? type =
			route.Mot?.Type;


		if (string.Equals(
			type,
			"Footpath",
			StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.Walk;
		}


		if (type?.StartsWith(
			"Mobility",
			StringComparison.OrdinalIgnoreCase)
			== true)
		{
			return TransferKind.Accessibility;
		}


		if (string.Equals(
			type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.Waiting;
		}


		if (arrival?.Station.Id is { } arrivalId
			&& departure?.Station.Id is { } departureId
			&& !string.Equals(
				arrivalId,
				departureId,
				StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.Walk;
		}


		if (!string.Equals(
				arrival?.Platform,
				departure?.Platform,
				StringComparison.OrdinalIgnoreCase)
			|| (arrival?.Platform is not null
				&& arrival.PlatformKind != departure?.PlatformKind
				&& arrival.PlatformKind != PlatformKind.Unknown
				&& departure?.PlatformKind != PlatformKind.Unknown))
		{
			return TransferKind.PlatformChange;
		}


		return TransferKind.SameStop;
	}


	private static JourneyLeg MapLeg(
	VvoPartialRoute route,
	Station? previousDestination,
	Station? nextOrigin,
	bool routeCancelled,
	IReadOnlyList<(double Latitude, double Longitude)> path)
	{
		StopTime[] stops =
			route.RegularStops
				.Select(MapStop)
				.ToArray();


		Station from;
		Station to;


		if (stops.Length > 0)
		{
			from =
				stops[0].Station;

			to =
				stops[^1].Station;
		}
		else
		{
			Station fallback =
				previousDestination
				?? nextOrigin
				?? throw new InvalidOperationException(
					"Cannot determine location of VVO non-stop leg.");

			from =
				fallback;

			to =
				nextOrigin
				?? fallback;
		}


		StopTime? firstStop =
			stops.FirstOrDefault();


		StopTime? lastStop =
			stops.LastOrDefault();


		return new JourneyLeg
		{
			Mode =
				MapMode(route.Mot),

			From =
				from,

			To =
				to,

			Stops =
				stops,

			Line =
				MapLine(route),

			Vehicle =
				MapVehicle(route),

			ScheduledDeparture =
				firstStop?.ScheduledDeparture,

			RealtimeDeparture =
				firstStop?.RealtimeDeparture,

			ScheduledArrival =
				lastStop?.ScheduledArrival,

			RealtimeArrival =
				lastStop?.RealtimeArrival,

			ProviderData =
				route,

			DeparturePlatform =
				firstStop?.Platform,

			DeparturePlatformKind =
				firstStop?.PlatformKind
				?? PlatformKind.Unknown,

			ArrivalPlatform =
				lastStop?.Platform,

			ArrivalPlatformKind =
				lastStop?.PlatformKind
				?? PlatformKind.Unknown,

			IsCancelled =
				route.TripCancelled
				|| routeCancelled,

			Path =
				path,

			Id =
	route.PartialRouteId.ToString(
		System.Globalization.CultureInfo.InvariantCulture),

			Notices =
				VvoNoticeParser.Parse(
					route.Infos)
		};
	}


	private static StopTime MapStop(
		VvoStop stop)
	{
		(double? latitude, double? longitude) = MapCoordinates(stop);

		return new StopTime
		{
			Station =
	new Station
	{
		Id =
			stop.DataId
			?? string.Empty,

		Name =
			stop.Name
			?? string.Empty,

		Place =
			stop.Place,

		Latitude =
			latitude,

		Longitude =
			longitude,

		Platform =
			stop.Platform?.Name,

		PlatformKind =
			StopLabel.KindOf(
				stop.Platform?.Type)
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

			PlatformKind =
				StopLabel.KindOf(
					stop.Platform?.Type),

			ProviderData =
				stop,

			IsCancelled =
				stop.ArrivalState == "Cancelled"
				|| stop.DepartureState == "Cancelled",

			Occupancy =
				MapOccupancy(
					stop.Occupancy)
		};
	}

	private static (
	double? Latitude,
	double? Longitude)
MapCoordinates(
	VvoStop stop)
	{
		if (stop.Latitude <= 0
			|| stop.Longitude <= 0)
		{
			return (null, null);
		}

		(double latitude, double longitude) =
			ConvertGk4ToWgs84(
				stop.Longitude,
				stop.Latitude);

		return (
			Latitude: latitude,
			Longitude: longitude);
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
				route.Mot.Direction,

			DirectionId =
				route.Mot.Diva?.Number
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

			Name =
				route.Mot.TrainNumber
				?? route.Mot.Name,

			Operator =
				route.Mot.TransportationCompany
				?? route.Mot.Diva?.Network,

			OperatorCode =
				route.Mot.OperatorCode,

			ProductName =
				route.Mot.ProductName,

			DlId =
				route.Mot.DlId,

			StatelessId =
				route.Mot.StatelessId,

			Occupancy =
				MapOccupancy(
					route.Mot.Occupancy)
		};
	}


	private static TransitMode MapMode(
		VvoMot? mot)
	{
		if (mot is null)
		{
			return TransitMode.Unknown;
		}


		if (string.Equals(
			mot.Type,
			"Footpath",
			StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.Walk;
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
			|| value.Contains("suburban")
			|| string.Equals(
				mot.Type,
				"RapidTransit",
				StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.SuburbanRail;
		}


		if (string.Equals(
			mot.Type,
			"Train",
			StringComparison.OrdinalIgnoreCase))
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


	private static OccupancyLevel MapOccupancy(
		string? occupancy)
	{
		if (string.IsNullOrWhiteSpace(
			occupancy))
		{
			return OccupancyLevel.Unknown;
		}


		return occupancy
			.Trim()
			.ToLowerInvariant()
			switch
		{
			"verylow" or "manyseats" =>
				OccupancyLevel.VeryLow,

			"low" or "fewseats" =>
				OccupancyLevel.Low,

			"medium" =>
				OccupancyLevel.Medium,

			"high" or "standingonly" =>
				OccupancyLevel.High,

			"full" =>
				OccupancyLevel.Full,

			"veryhigh" or "overloaded" =>
				OccupancyLevel.Overloaded,

			_ =>
				OccupancyLevel.Unknown
		};
	}

	private static IReadOnlyList<(double Latitude, double Longitude)> MapPath(
		VvoRoute route,
		VvoPartialRoute partialRoute)
	{
		int index =
			partialRoute.MapDataIndex;

		if (index < 0
			|| index >= route.MapData.Count)
		{
			return [];
		}

		string? mapData =
			route.MapData[index];

		if (string.IsNullOrWhiteSpace(mapData))
		{
			return [];
		}

		string[] values =
			mapData.Split(
				'|',
				StringSplitOptions.RemoveEmptyEntries);

		if (values.Length < 3)
		{
			return [];
		}

		var path =
			new List<(double Latitude, double Longitude)>(
				(values.Length - 1) / 2);

		for (int i = 1;
	i + 1 < values.Length;
	i += 2)
		{
			if (!double.TryParse(
				values[i],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out double x))
			{
				continue;
			}

			if (!double.TryParse(
				values[i + 1],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out double y))
			{
				continue;
			}

			path.Add(
				ConvertGk4ToWgs84(
					y,
					x));
		}

		return path;
	}

	private static (double Latitude, double Longitude) ConvertGk4ToWgs84(
	double x,
	double y)
	{
		double[] result =
			Gk4ToWgs84.MathTransform.Transform(
				new[]
				{
				x,
				y
				});

		return (
			Latitude: result[1],
			Longitude: result[0]);
	}

	private static void DebugTransfer(
	VvoPartialRoute route,
	StopTime? departureStop,
	StopTime? arrivalStop,
	int pathCount)
	{
		System.Diagnostics.Debug.WriteLine(
			$"""
			[VVO TRANSFER]
			PartialRouteId: {route.PartialRouteId}
			Duration raw: {route.Duration} min
			Kind: {route.Mot?.Type} / {route.Mot?.Name}
			Stops: {route.RegularStops.Count}
			Path points: {pathCount}

			Previous:
			  Station: {arrivalStop?.Station.Name}
			  Id: {arrivalStop?.Station.Id}
			  Arrival: {arrivalStop?.ScheduledArrival:o}
			  Departure: {arrivalStop?.ScheduledDeparture:o}
			  Platform: {arrivalStop?.Platform}

			Next:
			  Station: {departureStop?.Station.Name}
			  Id: {departureStop?.Station.Id}
			  Arrival: {departureStop?.ScheduledArrival:o}
			  Departure: {departureStop?.ScheduledDeparture:o}
			  Platform: {departureStop?.Platform}

			Infos:
			  {string.Join(" | ", route.Infos)}
			""");
	}
}