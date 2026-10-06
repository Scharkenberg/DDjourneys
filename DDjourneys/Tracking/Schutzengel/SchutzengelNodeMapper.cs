using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using static DDjourneys.Tracking.Schutzengel.SchutzengelWireCodes;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Stop nodes and map points (coordinates) of the raw data.
/// </summary>
internal static class SchutzengelNodeMapper
{
	internal static JsonObject NodeObject(
		VvoStop stop,
		JourneyLeg leg,
		int stopIndex,
		DateTimeOffset serverTime)
	{
		Station? station =
			FindStation(
				leg,
				stop,
				stopIndex);


		double latitude =
			station?.Latitude
			?? 0;

		double longitude =
			station?.Longitude
			?? 0;


		return new JsonObject
		{
			["id"] = stop.DataId ?? station?.Id ?? string.Empty,
			["name"] = stop.Name ?? station?.Name ?? string.Empty,
			["city"] =
				string.IsNullOrWhiteSpace(stop.Place)
					? station?.Place ?? "Dresden"
					: stop.Place,
			["type"] = TrafficNodeType(stop.Type),
			["location"] =
				new JsonObject
				{
					["latitude"] = latitude,
					["longitude"] = longitude,
					["projection"] = MapProjectionWgs84
				},
			["weather"] = new JsonArray(),
			["servingLines"] = new JsonArray(),
			["arrivalDateTime"] = stop.ArrivalTime,
			["departureDateTime"] = stop.DepartureTime,
			["actualArrivalDateTime"] = stop.ArrivalRealTime ?? stop.ArrivalTime,
			["actualDepartureDateTime"] = stop.DepartureRealTime ?? stop.DepartureTime,
			["platform"] =
				new JsonObject
				{
					["name"] = stop.Platform?.Name ?? string.Empty,
					["type"] = PlatformType(stop.Platform?.Type)
				},
			["positionInSequence"] = StopSequenceOnward,
			["tariffZone"] = new JsonArray(),
			["occupancy"] = Occupancy(stop.Occupancy),
			["cancelled"] =
				VvoStopStates.IsCancelled(stop.ArrivalState)
				|| VvoStopStates.IsCancelled(stop.DepartureState),
			["metaData"] = new JsonObject { ["serverTime"] = serverTime }
		};
	}


	internal static Station? FindStation(
		JourneyLeg leg,
		VvoStop stop,
		int stopIndex)
	{
		if (!string.IsNullOrWhiteSpace(
			stop.DataId))
		{
			Station? byId =
				leg.Stops
					.Select(
						stopTime =>
							stopTime.Station)
					.FirstOrDefault(
						station =>
							string.Equals(
								station.Id,
								stop.DataId,
								StringComparison.OrdinalIgnoreCase));


			if (byId is not null)
			{
				return byId;
			}
		}


		if (stopIndex >= 0
			&& stopIndex < leg.Stops.Count)
		{
			return leg.Stops[stopIndex].Station;
		}


		return null;
	}


	internal static JsonObject PointObject(
		(double Latitude, double Longitude) point) =>
		new()
		{
			["latitude"] = point.Latitude,
			["longitude"] = point.Longitude,
			["projection"] = MapProjectionWgs84
		};
}
