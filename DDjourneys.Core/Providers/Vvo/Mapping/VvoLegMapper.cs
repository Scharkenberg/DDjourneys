using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// One movement leg of a VVO route: stops, line, vehicle, times, platforms.
/// </summary>
public static class VvoLegMapper
{
	public static JourneyLeg MapLeg(
	VvoPartialRoute route,
	Station? previousDestination,
	Station? nextOrigin,
	bool routeCancelled,
	IReadOnlyList<(double Latitude, double Longitude)> path)
	{
		StopTime[] stops =
			route.RegularStops
				.Select(VvoStopMapper.MapStop)
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
				VvoModeMapper.MapMode(route.Mot),

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

	public static TransitLine? MapLine(
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
				VvoModeMapper.MapMode(route.Mot),

			Operator =
				route.Mot.TransportationCompany
				?? route.Mot.Diva?.Network,

			Destination =
				route.Mot.Direction,

			DirectionId =
				route.Mot.Diva?.Number
		};
	}

	public static Vehicle? MapVehicle(
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
				VvoModeMapper.MapOccupancy(
					route.Mot.Occupancy)
		};
	}
}
