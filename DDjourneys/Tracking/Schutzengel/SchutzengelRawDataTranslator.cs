using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Serialization;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using static DDjourneys.Tracking.Schutzengel.SchutzengelWireCodes;
using static DDjourneys.Tracking.Schutzengel.SchutzengelTariffMapper;
using static DDjourneys.Tracking.Schutzengel.SchutzengelNodeMapper;
using static DDjourneys.Tracking.Schutzengel.SchutzengelTransitionMapper;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Reconstructs the VVO Connection-shaped object used by the
/// DVB Schutzengel implementation as rawData. Orchestrates the
/// sub-mappers (wire codes, nodes, transitions, tariff).
/// </summary>
internal static class SchutzengelRawDataTranslator
{
	public static JsonObject Translate(
		VvoRoute route,
		Journey journey,
		string? sessionId,
		VvoStatus? status)
	{
		ArgumentNullException.ThrowIfNull(route);
		ArgumentNullException.ThrowIfNull(journey);


		var movementParts =
			new List<
				(
					int RawIndex,
					VvoPartialRoute Route,
					JourneyLeg Leg
				)>();


		int legIndex = 0;


		for (int rawIndex = 0;
			rawIndex < route.PartialRoutes.Count;
			rawIndex++)
		{
			VvoPartialRoute partialRoute =
				route.PartialRoutes[rawIndex];


			if (!IsMovement(partialRoute))
			{
				continue;
			}


			if (legIndex >= journey.Legs.Count)
			{
				break;
			}


			movementParts.Add(
				(
					rawIndex,
					partialRoute,
					journey.Legs[legIndex]));

			legIndex++;
		}


		var partialConnections =
			new JsonArray();


		DateTimeOffset serverTime =
			DateTimeOffset.UtcNow;


		for (int movementIndex = 0;
			movementIndex < movementParts.Count;
			movementIndex++)
		{
			(int movementRawIndex, VvoPartialRoute movementRoute, JourneyLeg movementLeg) =
				movementParts[movementIndex];


			int? nextRawIndex =
				movementIndex + 1
				< movementParts.Count
					? movementParts[movementIndex + 1].RawIndex
					: null;


			IReadOnlyList<JsonObject> transitions =
				BuildTransitions(
					route,
					journey,
					movementIndex,
					movementRawIndex,
					nextRawIndex);


			partialConnections.Add(
				PartialConnectionObject(
					movementRoute,
					movementLeg,
					transitions,
					serverTime));
		}


		return new JsonObject
		{
			["id"] = route.RouteId.ToString(CultureInfo.InvariantCulture),
			["price"] = ParsePrice(route.Price),
			["tariffInformation"] =
				new JsonObject
				{
					["network"] = route.Net ?? string.Empty,
					["priceLevel"] = route.NumberOfFareZones ?? string.Empty,
					["fromZone"] = route.FareZoneOrigin,
					["toZone"] = route.FareZoneDestination,
					["zoneStrings"] =
						Wire.Array(ZoneStrings(route.FareZoneNames).Select(zone => (JsonNode?)zone))
				},
			["partialConnections"] = partialConnections,
			["metaData"] =
				new JsonObject
				{
					["mmType"] = "oev",
					["serverTime"] = serverTime,
					["status"] =
						new JsonObject
						{
							["Code"] = status?.Code,
							["Message"] = status?.Message
						},
					["routeCancelled"] = route.RouteCancelled,
					["ticketNotes"] = route.TicketNotes
				},
			["sessionId"] = sessionId ?? string.Empty,
			["requestId"] = string.Empty,
			["distance"] = 0,
			["ticketInformation"] = new JsonArray()
		};
	}


	public static string Serialize(
		VvoRoute route,
		Journey journey,
		string? sessionId,
		VvoStatus? status) =>
		Translate(
			route,
			journey,
			sessionId,
			status)
			.ToJsonString();


	private static JsonObject PartialConnectionObject(
		VvoPartialRoute route,
		JourneyLeg leg,
		IReadOnlyList<JsonObject> transitions,
		DateTimeOffset serverTime)
	{
		return new JsonObject
		{
			["id"] = leg.Id ?? route.PartialRouteId.ToString(CultureInfo.InvariantCulture),
			["mot"] =
				new JsonObject
				{
					["type"] = MotType(leg.Mode),
					["category"] = TransportationCategoryMot,
					["providers"] = new JsonArray()
				},
			["line"] =
				new JsonObject
				{
					["direction"] =
						new JsonObject
						{
							["name"] =
								route.Mot?.Direction?.Trim()
								?? leg.Line?.Destination
								?? string.Empty
						},
					["name"] = route.Mot?.Name ?? leg.Line?.Name ?? string.Empty
				},
			["nodes"] =
				Wire.Array(
					route.RegularStops.Select(
						(stop, index) =>
							(JsonNode?)NodeObject(stop, leg, index, serverTime))),
			["disruptions"] =
				Wire.Array(
					(route.Mot?.Changes ?? [])
						.Select(id => (JsonNode?)new JsonObject { ["id"] = id })),
			["transitions"] = Wire.Array(transitions.Select(item => (JsonNode?)item)),
			["additionalInfo"] = Wire.Array(route.Infos.Select(info => (JsonNode?)info)),
			["serviceHotLine"] = route.BookingLink,
			["connectionAtRisk"] = false,
			["pathOnMap"] =
				Wire.Array(leg.Path.Select(point => (JsonNode?)PointObject(point))),
			["realTimeControlled"] = IsRealtimeControlled(route),
			["turnByTurn"] = new JsonArray(),
			["cancelled"] = leg.IsCancelled,
			["metaData"] =
				new JsonObject
				{
					["serverTime"] = serverTime,
					["tripCancelled"] = route.TripCancelled,
					["changeoverEndangered"] = route.ChangeoverEndangered
				}
		};
	}


	private static bool IsRealtimeControlled(
		VvoPartialRoute route)
	{
		if (route.RegularStops.Count == 0)
		{
			return false;
		}


		VvoStop first =
			route.RegularStops[0];

		VvoStop last =
			route.RegularStops[^1];


		return first.DepartureState is not null
			|| last.ArrivalState is not null;
	}
}
