using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>
/// Reconstructs the VVO Connection-shaped object used by the
/// DVB Schutzengel implementation as rawData.
/// </summary>
internal static class SchutzengelRawDataTranslator
{
	private const int TransportationCategoryMot = 0;
	private const int TransportationCategoryTransition = 1;

	private const int TrafficNodeTypeStop = 1;
	private const int TrafficNodeTypePoi = 2;
	private const int TrafficNodeTypeAddress = 3;

	private const int StopSequenceOnward = 2;

	private const int PlatformTypeAny = 0;
	private const int PlatformTypePlatform = 1;
	private const int PlatformTypeRailtrack = 2;

	private const int MotTypeAny = 0;
	private const int MotTypeTram = 1;
	private const int MotTypeSubway = 3;
	private const int MotTypeBus = 4;
	private const int MotTypeBusRegional = 7;
	private const int MotTypeBusIntercity = 8;
	private const int MotTypeBusNightline = 9;
	private const int MotTypeTrain = 11;
	private const int MotTypeTrainUrban = 12;
	private const int MotTypeHailedSharedTaxi = 16;
	private const int MotTypeTaxi = 17;
	private const int MotTypeWalking = 19;
	private const int MotTypeFerry = 25;
	private const int MotTypeCableway = 26;

	private const int TransitionTypeAny = 0;
	private const int TransitionTypeWalking = 1;
	private const int TransitionTypeStairsUp = 2;
	private const int TransitionTypeStairsDown = 3;
	private const int TransitionTypeElevatorUp = 4;
	private const int TransitionTypeElevatorDown = 5;
	private const int TransitionTypeEscalatorUp = 6;
	private const int TransitionTypeEscalatorDown = 7;
	private const int TransitionTypeRampUp = 8;
	private const int TransitionTypeRampDown = 9;
	private const int TransitionTypeStayInVehicle = 10;
	private const int TransitionTypeChangeVehicles = 11;
	private const int TransitionTypeEnsuredConnection = 12;

	private const int OccupancyUnknown = 0;
	private const int OccupancyManySeats = 2;
	private const int OccupancyFewSeats = 3;
	private const int OccupancyStandingOnly = 4;
	private const int OccupancyFull = 6;

	private const int MapProjectionWgs84 = 1;


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


	private static IReadOnlyList<object> BuildTransitions(
		VvoRoute route,
		Journey journey,
		int movementIndex,
		int rawIndex,
		int? nextRawIndex)
	{
		var transferParts =
			new List<VvoPartialRoute>();


		if (nextRawIndex is { } nextIndex)
		{
			for (int i = rawIndex + 1;
				i < nextIndex;
				i++)
			{
				VvoPartialRoute candidate =
					route.PartialRoutes[i];


				if (IsMovement(candidate))
				{
					break;
				}


				transferParts.Add(
					candidate);
			}
		}


		JourneyTransfer[] journeyTransfers =
			journey.Transfers
				.Where(
					transfer =>
						transfer.PreviousLegIndex
							== movementIndex
						&& transfer.NextLegIndex
							== movementIndex + 1)
				.ToArray();


		var result =
			new List<object>();


		for (int index = 0;
			index < transferParts.Count;
			index++)
		{
			VvoPartialRoute transferPart =
				transferParts[index];


			JourneyTransfer? journeyTransfer =
				index < journeyTransfers.Length
					? journeyTransfers[index]
					: null;


			object[] path =
				journeyTransfer is not null
				&& journeyTransfer.Path.Count > 0
					? journeyTransfer.Path
						.Select(PointObject)
						.ToArray()
					: Array.Empty<object>();


			result.Add(
				new
				{
					duration =
						Math.Max(
							0,
							transferPart.Duration),

					sections =
						new[]
						{
							new
							{
								type =
									TransitionType(
										transferPart.Mot?.Type),

								category =
									TransportationCategoryTransition,

								pathOnMap =
									path
							}
						}
				});
		}


		if (result.Count == 0
			&& nextRawIndex is not null
			&& movementIndex + 1 < journey.Legs.Count)
		{
			JourneyLeg previous =
				journey.Legs[movementIndex];

			JourneyLeg next =
				journey.Legs[movementIndex + 1];


			if (string.Equals(
				previous.To.Id,
				next.From.Id,
				StringComparison.OrdinalIgnoreCase))
			{
				int syntheticDuration =
					SyntheticVehicleChangeDuration(
						previous,
						next);


				result.Add(
					new
					{
						duration =
							syntheticDuration,

						sections =
							new[]
							{
								new
								{
									type =
										TransitionTypeChangeVehicles,

									category =
										TransportationCategoryTransition,

									pathOnMap =
										Array.Empty<object>()
								}
							}
					});
			}
		}


		return result;
	}


	private static object NodeObject(
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


		return new
		{
			id =
				stop.DataId
				?? station?.Id
				?? string.Empty,

			name =
				stop.Name
				?? station?.Name
				?? string.Empty,

			city =
				string.IsNullOrWhiteSpace(
					stop.Place)
					? station?.Place
						?? "Dresden"
					: stop.Place,

			type =
				TrafficNodeType(
					stop.Type),

			location =
				new
				{
					latitude,
					longitude,
					projection =
						MapProjectionWgs84
				},

			weather =
				Array.Empty<object>(),

			servingLines =
				Array.Empty<object>(),

			arrivalDateTime =
				stop.ArrivalTime,

			departureDateTime =
				stop.DepartureTime,

			actualArrivalDateTime =
				stop.ArrivalRealTime
				?? stop.ArrivalTime,

			actualDepartureDateTime =
				stop.DepartureRealTime
				?? stop.DepartureTime,

			platform =
				new
				{
					name =
						stop.Platform?.Name
						?? string.Empty,

					type =
						PlatformType(
							stop.Platform?.Type)
				},

			positionInSequence =
				StopSequenceOnward,

			tariffZone =
				Array.Empty<object>(),

			occupancy =
				Occupancy(
					stop.Occupancy),

			cancelled =
				stop.ArrivalState == "Cancelled"
				|| stop.DepartureState == "Cancelled",

			metaData =
				new
				{
					serverTime
				}
		};
	}


	private static Station? FindStation(
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


	private static bool IsMovement(
		VvoPartialRoute route) =>
		route.RegularStops.Count > 0;


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


	private static int SyntheticVehicleChangeDuration(
		JourneyLeg previous,
		JourneyLeg next)
	{
		DateTimeOffset? previousTime =
			previous.Stops
				.LastOrDefault()
				?.RealtimeArrival
			?? previous.Stops
				.LastOrDefault()
				?.ScheduledArrival
			?? previous.EffectiveArrival;


		DateTimeOffset? nextTime =
			next.Stops
				.FirstOrDefault()
				?.RealtimeDeparture
			?? next.Stops
				.FirstOrDefault()
				?.ScheduledDeparture
			?? next.EffectiveDeparture;


		if (previousTime is not { } previousValue
			|| nextTime is not { } nextValue)
		{
			return 0;
		}


		return Math.Max(
			0,
			(int)
				Math.Truncate(
					(nextValue - previousValue)
						.TotalMinutes));
	}


	private static int TransitionType(
		string? type) =>
		type switch
		{
			"Footpath" =>
				TransitionTypeWalking,

			"StayForConnection" =>
				TransitionTypeEnsuredConnection,

			"StayInVehicle" =>
				TransitionTypeStayInVehicle,

			"MobilityRampUp" =>
				TransitionTypeRampUp,

			"MobilityRampDown" =>
				TransitionTypeRampDown,

			"MobilityStairsUp" =>
				TransitionTypeStairsUp,

			"MobilityStairsDown" =>
				TransitionTypeStairsDown,

			"MobilityElevatorUp" =>
				TransitionTypeElevatorUp,

			"MobilityElevatorDown" =>
				TransitionTypeElevatorDown,

			"MobilityEscalatorUp" =>
				TransitionTypeEscalatorUp,

			"MobilityEscalatorDown" =>
				TransitionTypeEscalatorDown,

			_ =>
				TransitionTypeAny
		};


	private static int MotType(
		TransitMode mode) =>
		mode switch
		{
			TransitMode.Tram =>
				MotTypeTram,

			TransitMode.Bus =>
				MotTypeBus,

			TransitMode.Subway =>
				MotTypeSubway,

			TransitMode.SuburbanRail =>
				MotTypeTrainUrban,

			TransitMode.RegionalTrain =>
				MotTypeTrain,

			TransitMode.LongDistanceTrain =>
				MotTypeTrain,

			TransitMode.Ferry =>
				MotTypeFerry,

			TransitMode.CableCar =>
				MotTypeCableway,

			TransitMode.Taxi =>
				MotTypeTaxi,

			TransitMode.OnDemand =>
				MotTypeHailedSharedTaxi,

			TransitMode.Walk =>
				MotTypeWalking,

			_ =>
				MotTypeAny
		};


	private static int TrafficNodeType(
		string? type) =>
		type switch
		{
			"Poi" or "p" =>
				TrafficNodeTypePoi,

			"Address" or "a" or "c" =>
				TrafficNodeTypeAddress,

			_ =>
				TrafficNodeTypeStop
		};


	private static int PlatformType(
		string? type) =>
		type switch
		{
			"Platform" =>
				PlatformTypePlatform,

			"Railtrack" =>
				PlatformTypeRailtrack,

			_ =>
				PlatformTypeAny
		};


	private static int Occupancy(
		string? occupancy) =>
		occupancy switch
		{
			"ManySeats" =>
				OccupancyManySeats,

			"FewSeats" =>
				OccupancyFewSeats,

			"StandingOnly" =>
				OccupancyStandingOnly,

			"Full" =>
				OccupancyFull,

			_ =>
				OccupancyUnknown
		};


	private static int ParsePrice(
		string? price)
	{
		if (string.IsNullOrWhiteSpace(
			price))
		{
			return 0;
		}


		return int.TryParse(
			price.Replace(
				",",
				string.Empty,
				StringComparison.Ordinal),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out int value)
			? value
			: 0;
	}


	private static string[] ZoneStrings(
		string? value)
	{
		if (string.IsNullOrWhiteSpace(
			value))
		{
			return
			[
				string.Empty,
				string.Empty
			];
		}


		string[] parts =
			value.Split(
				',',
				StringSplitOptions.None);


		string First(int index) =>
			index < parts.Length
				? Regex.Replace(
					parts[index],
					@"TZ\s|\(\d+\)",
					string.Empty)
					.Trim()
				: string.Empty;


		return
		[
			First(0),
			First(1)
		];
	}


	private static object PointObject(
		(double Latitude, double Longitude) point)
	{
		return new
		{
			latitude =
				point.Latitude,

			longitude =
				point.Longitude,

			projection =
				MapProjectionWgs84
		};
	}
}