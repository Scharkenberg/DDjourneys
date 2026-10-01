using System.Globalization;
using DDjourneys.Core.Models;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal static class SchutzengelRawDataTranslator
{
	public static object Translate(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		return new
		{
			id =
				journey.Id
				?? string.Empty,

			origin =
				Node(
					journey.From,
					journey.Departure),

			destination =
				Node(
					journey.To,
					journey.Arrival),

			startDateTime =
				ToIso(
					journey.Departure),

			endDateTime =
				ToIso(
					journey.Arrival),

			partialConnections =
				journey.Legs
					.Select(
						(leg, index) =>
							PartialConnection(
								journey,
								leg,
								index))
					.ToArray(),

			price = 0,
			distance = 0,
			sessionId = string.Empty,
			requestId = string.Empty,
			ticketInformation = Array.Empty<object>(),
			tariffInformation = new
			{
				network = string.Empty,
				priceLevel = string.Empty,
				fromZone = (int?)null,
				toZone = (int?)null,
				zoneStrings = new[] { string.Empty, string.Empty }
			},
			metaData = new
			{
				serverTime = DateTimeOffset.UtcNow,
				status = string.Empty,
				routeCancelled = journey.IsCancelled,
				ticketNotes = string.Empty
			}
		};
	}


	private static object PartialConnection(
		Journey journey,
		JourneyLeg leg,
		int index)
	{
		return new
		{
			id =
				leg.Id
				?? index.ToString(
					CultureInfo.InvariantCulture),

			mot = new
			{
				type =
					MotType(
						leg.Mode),

				category = "MOT",
				providers = Array.Empty<object>()
			},

			line = new
			{
				name =
					leg.Line?.Name
					?? string.Empty,

				direction = new
				{
					name =
						leg.Line?.Destination
						?? string.Empty
				}
			},

			nodes =
				BuildStops(
					leg)
					.Select(
						stop =>
							StopNode(
								stop))
					.ToArray(),

			pathOnMap =
				BuildPolyline(
					leg.Path),

			transitions =
				journey.Transfers
					.Where(
						transfer =>
							transfer.PreviousLegIndex == index)
					.Select(
						transfer =>
							new
							{
								duration =
									(int)Math.Round(
										transfer.Duration.TotalMinutes),

								sections =
									new[]
									{
										new
										{
											pathOnMap =
												transfer.Path
													.Select(
														point =>
															new
															{
																latitude = point.Latitude,
																longitude = point.Longitude,
																projection = "WGS84"
															})
													.ToArray()
										}
									}
							})
					.ToArray(),

			disruptions = Array.Empty<object>(),
			additionalInfo = leg.Notices.ToArray(),
			serviceHotLine = string.Empty,
			realTimeControlled = false,
			metaData = new
			{
				tripCancelled = leg.IsCancelled,
				changeoverEndangered = false
			}
		};
	}


	private static StopTime[] BuildStops(JourneyLeg leg)
	{
		if (leg.Stops.Count > 0)
		{
			var result = leg.Stops.ToList();

			if (!SameStation(result[0].Station, leg.From))
			{
				result.Insert(
					0,
					CreateSyntheticStop(
						leg.From,
						leg.ScheduledDeparture));
			}

			if (!SameStation(result[^1].Station, leg.To))
			{
				result.Add(
					CreateSyntheticStop(
						leg.To,
						leg.ScheduledArrival));
			}

			return result.ToArray();
		}

		return
		[
			CreateSyntheticStop(
				leg.From,
				leg.ScheduledDeparture),

			CreateSyntheticStop(
				leg.To,
				leg.ScheduledArrival)
		];
	}


	private static StopTime CreateSyntheticStop(
		Station station,
		DateTimeOffset? time)
	{
		return new StopTime
		{
			Station = station,
			ScheduledArrival = time,
			ScheduledDeparture = time,
			Platform = station.Platform
		};
	}


	private static object StopNode(StopTime stop)
	{
		return new
		{
			name = stop.Station.Name,
			id = stop.Station.Id,
			coords = Coordinates(stop.Station),
			departureDateTime = ToIso(stop.ScheduledDeparture),
			arrivalDateTime = ToIso(stop.ScheduledArrival),
			actualDepartureDateTime = ToIso(stop.RealtimeDeparture),
			actualArrivalDateTime = ToIso(stop.RealtimeArrival),
			platform = stop.Platform
		};
	}


	private static object Node(
		Station station,
		DateTimeOffset? time)
	{
		return new
		{
			name = station.Name,
			id = station.Id,
			coords = Coordinates(station),
			departureDateTime = ToIso(time),
			arrivalDateTime = ToIso(time),
			actualDepartureDateTime = ToIso(time),
			actualArrivalDateTime = ToIso(time),
			platform = station.Platform
		};
	}


	private static object Coordinates(Station station)
	{
		return new
		{
			lat = station.Latitude ?? 0,
			lon = station.Longitude ?? 0,
			projection = "WGS84"
		};
	}


	private static object[] BuildPolyline(
		IEnumerable<(double Latitude, double Longitude)> path)
	{
		return path
			.Select(
				point =>
					new
					{
						lat = point.Latitude,
						lon = point.Longitude,
						projection = "WGS84"
					})
			.ToArray();
	}


	private static string MotType(TransitMode mode) =>
		mode switch
		{
			TransitMode.Bus => "BUS",
			TransitMode.Tram => "TRAM",
			TransitMode.Subway => "SUBWAY",
			TransitMode.SuburbanRail => "TRAIN_URBAN",
			TransitMode.RegionalTrain => "TRAIN_RE",
			TransitMode.LongDistanceTrain => "TRAIN",
			TransitMode.Ferry => "FERRY",
			TransitMode.CableCar => "CABLEWAY",
			TransitMode.Taxi => "TAXI",
			TransitMode.OnDemand => "HAILEDSHAREDTAXI",
			_ => "UNKNOWN"
		};


	private static string? ToIso(DateTimeOffset? value) =>
		value?.ToString("O", CultureInfo.InvariantCulture);


	private static bool SameStation(Station left, Station right) =>
		string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
}