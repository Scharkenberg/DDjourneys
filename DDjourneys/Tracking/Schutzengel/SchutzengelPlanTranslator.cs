using System.Text.Json.Nodes;
using DDjourneys.Core.Serialization;
using DDjourneys.Core.Models;

namespace DDjourneys.Tracking.Schutzengel;

internal static class SchutzengelPlanTranslator
{
	public static JsonObject Translate(
		Journey journey,
		JsonNode rawData,
		SchutzengelOptions? options = null,
		string? tripReference = null)
	{
		ArgumentNullException.ThrowIfNull(
			journey);

		ArgumentNullException.ThrowIfNull(
			rawData);


		SchutzengelOptions alerts =
			options ?? SchutzengelOptions.Default;

		var episodes =
			new List<JsonObject>();


		for (int i = 0;
			i < journey.Legs.Count;
			i++)
		{
			JourneyLeg leg =
				journey.Legs[i];


			episodes.Add(
				MovementEpisode(
					leg));


			if (i < journey.Legs.Count - 1)
			{
				AddInterLegTransfer(
					episodes,
					journey,
					i,
					i + 1);
			}
		}


		return new JsonObject
		{
			["journey"] =
				new JsonObject
				{
					["episodes"] = Wire.Array(episodes.Select(episode => (JsonNode?)episode))
				},
			["rawData"] = rawData,
			["attentions"] =
				new JsonObject
				{
					["start"] =
						new JsonObject
						{
							["timeBeforeSeconds"] = alerts.StartLeadSeconds,
							["active"] = alerts.StartActive
						},
					["change"] = alerts.Change,
					["problem"] = alerts.Problem
				},
			["type"] = "static",

			// The trip of the plan this one replaces; absent (not null) otherwise, as the reference client sends it.
			["trip_reference"] = tripReference
		}.WithoutNulls("trip_reference");
	}


	public static string Serialize(
		Journey journey,
		JsonNode rawData,
		SchutzengelOptions? options = null,
		string? tripReference = null) =>
		Translate(
			journey,
			rawData,
			options,
			tripReference)
			.ToJsonString();


	private static JsonObject MovementEpisode(
		JourneyLeg leg)
	{
		return !leg.Mode.IsRide()
			? IndividualEpisode(
				leg)
			: PublicEpisode(
				leg);
	}


	private static JsonObject PublicEpisode(
		JourneyLeg leg)
	{
		StopTime[] stops =
			BuildStops(
				leg);


		if (stops.Length == 0)
		{
			throw new InvalidOperationException(
				"Schutzengel public episode has no stops.");
		}


		return new JsonObject
		{
			["api"] = "vvo",
			["allStations"] = Wire.Array(stops.Select(stop => (JsonNode?)StopObject(stop))),
			["from"] = StopObject(stops[0]),
			["to"] = StopObject(stops[^1]),
			["type"] = "public",
			["mot"] =
				new JsonObject
				{
					["type"] = MotType(leg.Mode),
					["name"] = leg.Line?.Name ?? string.Empty,
					["direction"] = leg.Line?.Destination ?? string.Empty
				},
			["id"] = leg.Id ?? string.Empty,
			["polyline"] = BuildPolyline(leg.Path)
		};
	}


	private static JsonObject IndividualEpisode(
		JourneyLeg leg)
	{
		DateTimeOffset? fromTime =
			leg.ScheduledDeparture
			?? leg.EffectiveDeparture;


		DateTimeOffset? toTime =
			leg.ScheduledArrival
			?? leg.EffectiveArrival;


		int durationSeconds =
			PlannedDurationSeconds(
				leg);


		return new JsonObject
		{
			["id"] = leg.Id ?? string.Empty,
			["mot"] =
				new JsonObject
				{
					["type"] = MotType(leg.Mode),
					["name"] = leg.Line?.Name ?? string.Empty,
					["direction"] = leg.Line?.Destination ?? string.Empty
				},
			["from"] = WalkingStationObject(leg.From, fromTime),
			["to"] = WalkingStationObject(leg.To, toTime),
			["type"] = "individual",
			["allStations"] =
				Wire.Array(
					WalkingStationObject(leg.From, fromTime),
					WalkingStationObject(leg.To, toTime)),
			["requiredTimeMS"] = durationSeconds * 1000,
			["durationSeconds"] = durationSeconds,
			["polyline"] = BuildPolyline(leg.Path)
		};
	}


	private static void AddInterLegTransfer(
		List<JsonObject> episodes,
		Journey journey,
		int previousLegIndex,
		int nextLegIndex)
	{
		JourneyLeg previousLeg =
			journey.Legs[previousLegIndex];

		JourneyLeg nextLeg =
			journey.Legs[nextLegIndex];


		JourneyTransfer[] transfers =
			journey.Transfers
				.Where(
					transfer =>
						transfer.PreviousLegIndex
							== previousLegIndex
						&& transfer.NextLegIndex
							== nextLegIndex)
				.ToArray();


		if (transfers.Length > 0)
		{
			Station from =
				previousLeg.To;

			Station to =
				nextLeg.From;


			TimeSpan transferDuration =
				TimeSpan.FromSeconds(
					transfers.Sum(
						transfer =>
							Math.Max(
								0,
								transfer.Duration
									.TotalSeconds)));


			var path =
				transfers
					.SelectMany(
						transfer =>
							transfer.Path);


			episodes.Add(
				WalkingEpisode(
					from,
					to,
					GetTransferDepartureTime(
						previousLeg),
					GetTransferArrivalTime(
						nextLeg),
					transferDuration,
					path));


			return;
		}


		if (!string.Equals(
			previousLeg.To.Id,
			nextLeg.From.Id,
			StringComparison.OrdinalIgnoreCase))
		{
			return;
		}


		DateTimeOffset? fromTime =
			GetTransferDepartureTime(
				previousLeg);

		DateTimeOffset? toTime =
			GetTransferArrivalTime(
				nextLeg);


		if (fromTime is not { }
			fromValue
			|| toTime is not { }
			toValue)
		{
			return;
		}


		TimeSpan syntheticDuration =
			toValue >= fromValue
				? toValue - fromValue
				: TimeSpan.Zero;


		episodes.Add(
			WalkingEpisode(
				previousLeg.To,
				nextLeg.From,
				fromValue,
				toValue,
				syntheticDuration,
				Array.Empty<
					(double Latitude, double Longitude)>()));
	}


	private static JsonObject WalkingEpisode(
		Station from,
		Station to,
		DateTimeOffset? fromTime,
		DateTimeOffset? toTime,
		TimeSpan duration,
		IEnumerable<
			(double Latitude, double Longitude)> path)
	{
		int seconds =
			Math.Max(
				0,
				(int)
					Math.Round(
						duration.TotalSeconds));


		return new JsonObject
		{
			["id"] = string.Empty,
			["mot"] =
				new JsonObject
				{
					["name"] = "Fussweg",
					["type"] = "WALKING",
					["direction"] = string.Empty
				},
			["from"] = WalkingStationObject(from, fromTime),
			["to"] = WalkingStationObject(to, toTime),
			["type"] = "individual",
			["allStations"] =
				Wire.Array(
					WalkingStationObject(from, fromTime),
					WalkingStationObject(to, toTime)),
			["requiredTimeMS"] = seconds * 1000,
			["durationSeconds"] = seconds,
			["polyline"] = BuildPolyline(path)
		};
	}


	private static StopTime[] BuildStops(
		JourneyLeg leg)
	{
		if (leg.Stops.Count > 0)
		{
			var result =
				leg.Stops.ToList();


			if (!SameStation(
				result[0].Station,
				leg.From))
			{
				result.Insert(
					0,
					CreateSyntheticStop(
						leg.From,
						leg.ScheduledDeparture));
			}


			if (!SameStation(
				result[^1].Station,
				leg.To))
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
			Station =
				station,

			ScheduledArrival =
				time,

			ScheduledDeparture =
				time,

			Platform =
				station.Platform
		};
	}


	private static JsonObject StopObject(
		StopTime stop)
	{
		var result =
			new JsonObject
			{
				["name"] = stop.Station.Name,
				["id"] = stop.Station.Id,
				["coords"] = Coordinates(stop.Station),
				["scheduledTime"] = ToUnixMilliseconds(stop.ScheduledDeparture ?? stop.ScheduledArrival),
				["api"] = "vvo"
			};


		if (!string.IsNullOrWhiteSpace(
			stop.Platform))
		{
			result["platform"] =
				new JsonObject
				{
					// The reference client says "Gleis" for railtracks, "Steig" for everything else.
					["type"] = stop.PlatformKind == PlatformKind.Railtrack ? "Gleis" : "Steig",
					["name"] = stop.Platform
				};
		}


		return result;
	}


	private static JsonObject WalkingStationObject(
		Station station,
		DateTimeOffset? time) =>
		new()
		{
			["id"] = station.Id,
			["api"] = "vvo",
			["name"] = station.Name,
			["coords"] = Coordinates(station),
			["scheduledTime"] = ToUnixMilliseconds(time)
		};


	private static JsonObject Coordinates(
		Station station) =>
		new()
		{
			["lat"] = station.Latitude ?? 0,
			["lon"] = station.Longitude ?? 0,
			["projection"] = "WGS84"
		};


	private static long? ToUnixMilliseconds(
		DateTimeOffset? value) =>
		value?.ToUnixTimeMilliseconds();


	private static JsonArray BuildPolyline(
		IEnumerable<
			(double Latitude, double Longitude)> path) =>
		Wire.Array(
			path.Select(
				point =>
					(JsonNode?)new JsonObject
					{
						["lat"] = point.Latitude,
						["lon"] = point.Longitude,
						["projection"] = "WGS84"
					}));


	private static string MotType(
		TransitMode mode) =>
		mode switch
		{
			TransitMode.Bus =>
				"BUS",

			TransitMode.Tram =>
				"TRAM",

			TransitMode.Subway =>
				"SUBWAY",

			TransitMode.SuburbanRail =>
				"TRAIN_URBAN",

			TransitMode.RegionalTrain =>
				"TRAIN",

			TransitMode.LongDistanceTrain =>
				"TRAIN",

			TransitMode.Ferry =>
				"FERRY",

			TransitMode.CableCar =>
				"CABLEWAY",

			TransitMode.Taxi =>
				"TAXI",

			TransitMode.OnDemand =>
				"HAILEDSHAREDTAXI",

			TransitMode.Walk =>
				"WALKING",

			_ =>
				"ANY"
		};



	private static int PlannedDurationSeconds(
		JourneyLeg leg)
	{
		DateTimeOffset? departure =
			leg.ScheduledDeparture;

		DateTimeOffset? arrival =
			leg.ScheduledArrival;


		if (departure is not { }
			|| arrival is not { })
		{
			departure =
				leg.EffectiveDeparture;

			arrival =
				leg.EffectiveArrival;
		}


		if (departure is not { }
			departureValue
			|| arrival is not { }
			arrivalValue
			|| arrivalValue <= departureValue)
		{
			return 0;
		}


		return Math.Max(
			0,
			(int)
				Math.Round(
					(arrivalValue - departureValue)
						.TotalSeconds));
	}


	private static DateTimeOffset? GetTransferDepartureTime(
		JourneyLeg leg) =>
		leg.Stops
			.LastOrDefault()
			?.ScheduledDeparture
			?? leg.ScheduledArrival
			?? leg.EffectiveArrival;


	private static DateTimeOffset? GetTransferArrivalTime(
		JourneyLeg leg) =>
		leg.Stops
			.FirstOrDefault()
			?.ScheduledDeparture
			?? leg.ScheduledDeparture
			?? leg.EffectiveDeparture;


	private static bool SameStation(
		Station left,
		Station right) =>
		string.Equals(
			left.Id,
			right.Id,
			StringComparison.OrdinalIgnoreCase);
}