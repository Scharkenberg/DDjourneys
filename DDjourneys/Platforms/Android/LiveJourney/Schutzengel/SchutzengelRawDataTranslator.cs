using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using static DDjourneys.Platforms.Android.LiveJourney.Schutzengel.SchutzengelWireCodes;
using static DDjourneys.Platforms.Android.LiveJourney.Schutzengel.SchutzengelTariffMapper;
using static DDjourneys.Platforms.Android.LiveJourney.Schutzengel.SchutzengelNodeMapper;
using static DDjourneys.Platforms.Android.LiveJourney.Schutzengel.SchutzengelTransitionMapper;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>
/// Reconstructs the VVO Connection-shaped object used by the
/// DVB Schutzengel implementation as rawData. Orchestrates the
/// sub-mappers (wire codes, nodes, transitions, tariff).
/// </summary>
internal static class SchutzengelRawDataTranslator
{
	public static object Translate(
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
			new List<object>();


		DateTimeOffset serverTime =
			DateTimeOffset.UtcNow;


		for (int movementIndex = 0;
			movementIndex < movementParts.Count;
			movementIndex++)
		{
			var movement =
				movementParts[movementIndex];


			int? nextRawIndex =
				movementIndex + 1
				< movementParts.Count
					? movementParts[movementIndex + 1].RawIndex
					: null;


			IReadOnlyList<object> transitions =
				BuildTransitions(
					route,
					journey,
					movementIndex,
					movement.RawIndex,
					nextRawIndex);


			partialConnections.Add(
				PartialConnectionObject(
					movement.Route,
					movement.Leg,
					transitions,
					serverTime));
		}


		return new
		{
			id =
				route.RouteId.ToString(
					CultureInfo.InvariantCulture),

			price =
				ParsePrice(
					route.Price),

			tariffInformation =
				new
				{
					network =
						route.Net
						?? string.Empty,

					priceLevel =
						route.NumberOfFareZones
						?? string.Empty,

					fromZone =
						route.FareZoneOrigin,

					toZone =
						route.FareZoneDestination,

					zoneStrings =
						ZoneStrings(
							route.FareZoneNames)
				},

			partialConnections,

			metaData =
				new
				{
					mmType = "oev",

					serverTime,

					status =
						new
						{
							Code =
								status?.Code,

							Message =
								status?.Message
						},

					routeCancelled =
						route.RouteCancelled,

					ticketNotes =
						route.TicketNotes
				},

			sessionId =
				sessionId
				?? string.Empty,

			requestId =
				string.Empty,

			distance = 0,

			ticketInformation =
				Array.Empty<object>()
		};
	}


	public static string Serialize(
		VvoRoute route,
		Journey journey,
		string? sessionId,
		VvoStatus? status) =>
		JsonSerializer.Serialize(
			Translate(
				route,
				journey,
				sessionId,
				status));


	private static object PartialConnectionObject(
		VvoPartialRoute route,
		JourneyLeg leg,
		IReadOnlyList<object> transitions,
		DateTimeOffset serverTime)
	{
		return new
		{
			id =
				leg.Id
				?? route.PartialRouteId.ToString(
					CultureInfo.InvariantCulture),

			mot =
				new
				{
					type =
						MotType(
							leg.Mode),

					category =
						TransportationCategoryMot,

					providers =
						Array.Empty<object>()
				},

			line =
				new
				{
					direction =
						new
						{
							name =
								route.Mot?.Direction?.Trim()
								?? leg.Line?.Destination
								?? string.Empty
						},

					name =
						route.Mot?.Name
						?? leg.Line?.Name
						?? string.Empty
				},

			nodes =
				route.RegularStops
					.Select(
						(stop, index) =>
							NodeObject(
								stop,
								leg,
								index,
								serverTime))
					.ToArray(),

			disruptions =
				route.Mot?.Changes
					.Select(
						id =>
							new
							{
								id
							})
					.ToArray()
				?? Array.Empty<object>(),

			transitions,

			additionalInfo =
				route.Infos,

			serviceHotLine =
				route.BookingLink,

			connectionAtRisk = false,

			pathOnMap =
				leg.Path
					.Select(
						PointObject)
					.ToArray(),

			realTimeControlled =
				IsRealtimeControlled(
					route),

			turnByTurn =
				Array.Empty<object>(),

			cancelled =
				leg.IsCancelled,

			metaData =
				new
				{
					serverTime,

					tripCancelled =
						route.TripCancelled,

					changeoverEndangered =
						route.ChangeoverEndangered
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
