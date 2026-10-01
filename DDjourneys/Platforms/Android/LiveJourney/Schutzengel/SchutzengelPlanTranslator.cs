using DDjourneys.Core.Models;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal static class SchutzengelPlanTranslator
{
	public static object Translate(
		Journey journey,
		object rawData,
		string tripReference)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(rawData);
		ArgumentNullException.ThrowIfNull(tripReference);

		var episodes = new List<object>();

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			if (i == 0)
			{
				AddTerminalTransfer(
					episodes,
					journey,
					previousLegIndex: null,
					nextLegIndex: 0);
			}
			else
			{
				AddTransfer(
					episodes,
					journey,
					i - 1,
					i);
			}

			episodes.Add(
	PublicEpisode(
		journey.Legs[i]));
		}

		AddTerminalTransfer(
			episodes,
			journey,
			previousLegIndex: journey.Legs.Count - 1,
			nextLegIndex: null);

		return new
		{
			journey = new
			{
				episodes
			},

			rawData,

			trip_reference = tripReference,

			attentions = new
			{
				start = new
				{
					timeBeforeSeconds = 300,
					active = true
				},

				change = true,
				problem = true
			},

			type = "static"
		};
	}

	public static string Serialize(
		Journey journey,
		object rawData,
		string tripReference) =>
		JsonSerializer.Serialize(
			Translate(
				journey,
				rawData,
				tripReference));

	private static void AddTransfer(
		List<object> episodes,
		Journey journey,
		int previousLegIndex,
		int nextLegIndex)
	{
		JourneyTransfer? transfer =
			journey.Transfers.FirstOrDefault(
				t =>
					t.PreviousLegIndex == previousLegIndex
					&& t.NextLegIndex == nextLegIndex
					&& t.Kind == TransferKind.Walk);

		if (transfer is null)
		{
			return;
		}

		episodes.Add(
			WalkingEpisode(
				GetTransferFrom(
					journey,
					transfer),

				GetTransferTo(
					journey,
					transfer),

				transfer.Duration));
	}


	private static void AddTerminalTransfer(
		List<object> episodes,
		Journey journey,
		int? previousLegIndex,
		int? nextLegIndex)
	{
		JourneyTransfer? transfer =
			journey.Transfers.FirstOrDefault(
				t =>
					t.PreviousLegIndex == previousLegIndex
					&& t.NextLegIndex == nextLegIndex
					&& t.Kind == TransferKind.Walk);

		if (transfer is null)
		{
			return;
		}

		episodes.Add(
			WalkingEpisode(
				GetTransferFrom(
					journey,
					transfer),

				GetTransferTo(
					journey,
					transfer),

				transfer.Duration));
	}


	private static object PublicEpisode(
		JourneyLeg leg)
	{
		StopTime[] stops =
			BuildStops(leg);

		if (stops.Length == 0)
		{
			throw new InvalidOperationException(
				"Schutzengel public episode has no stops.");
		}

		return new
		{
			api = "vvo",

			allStations =
				stops.Select(
					StopObject),

			from =
				StopObject(
					stops[0]),

			to =
				StopObject(
					stops[^1]),

			type = "public",

			mot = new
			{
				type =
					MotType(
						leg.Mode),

				name =
					leg.Line?.Name
					?? string.Empty,

				direction =
					leg.Line?.Destination
					?? string.Empty
			},

			id = leg.Id	?? string.Empty,

			polyline =
				BuildPolyline(
					stops.Select(
						stop => stop.Station))
		};
	}


	private static object WalkingEpisode(
		Station from,
		Station to,
		TimeSpan duration)
	{
		int seconds =
			Math.Max(
				0,
				(int)Math.Round(
					duration.TotalSeconds));

		return new
		{
			id = string.Empty,

			to =
				WalkingStationObject(
					to,
					GetWalkingTime(
						to)),

			mot = new
			{
				name = "Fussweg",
				type = "WALKING",
				direction = string.Empty
			},

			from =
				WalkingStationObject(
					from,
					GetWalkingTime(
						from)),

			type = "individual",

			allStations = new[]
			{
				WalkingStationObject(
					from,
					GetWalkingTime(from)),

				WalkingStationObject(
					to,
					GetWalkingTime(to))
			},

			requiredTimeMS =
				seconds * 1000,

			durationSeconds =
				seconds,

			polyline =
				BuildPolyline(
					new[]
					{
						from,
						to
					})
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
			Station = station,
			ScheduledArrival = time,
			ScheduledDeparture = time,
			Platform = station.Platform
		};
	}


	private static object StopObject(
		StopTime stop)
	{
		Dictionary<string, object?> result =
			new()
			{
				["name"] =
					stop.Station.Name,

				["id"] =
					stop.Station.Id,

				["coords"] =
					Coordinates(
						stop.Station),

				["scheduledTime"] =
					ToUnixMilliseconds(
						stop.ScheduledDeparture
						?? stop.ScheduledArrival),

				["platform"] =
					PlatformObject(
						stop.Platform),

				["api"] =
					"vvo"
			};

		if (string.IsNullOrWhiteSpace(
			stop.Platform))
		{
			result.Remove(
				"platform");
		}

		return result;
	}


	private static object WalkingStationObject(
		Station station,
		DateTimeOffset? time)
	{
		return new
		{
			id = station.Id,
			api = "vvo",
			name = station.Name,
			coords = Coordinates(station),
			scheduledTime =
				ToUnixMilliseconds(time)
		};
	}


	private static object PlatformObject(
		string? platform)
	{
		return new
		{
			type = "Steig",
			name = platform ?? string.Empty
		};
	}


	private static object Coordinates(
		Station station)
	{
		return new
		{
			lat =
				station.Latitude ?? 0,

			lon =
				station.Longitude ?? 0,

			projection = "WGS84"
		};
	}


	private static long? ToUnixMilliseconds(
		DateTimeOffset? value)
	{
		return value?.ToUnixTimeMilliseconds();
	}


	private static object[] BuildPolyline(
		IEnumerable<Station> stations)
	{
		return stations
			.Where(
				s =>
					s.Latitude.HasValue
					&& s.Longitude.HasValue)
			.Select(
				s => new
				{
					lat = s.Latitude!.Value,
					lon = s.Longitude!.Value,
					projection = "WGS84"
				})
			.ToArray();
	}


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
				"TRAIN",

			TransitMode.RegionalTrain =>
				"TRAIN",

			TransitMode.LongDistanceTrain =>
				"TRAIN",

			TransitMode.Ferry =>
				"FERRY",

			TransitMode.CableCar =>
				"CABLECAR",

			TransitMode.Taxi =>
				"TAXI",

			TransitMode.OnDemand =>
				"ONDEMAND",

			_ =>
				"UNKNOWN"
		};


	private static Station GetTransferFrom(
		Journey journey,
		JourneyTransfer transfer)
	{
		if (transfer.PreviousLegIndex is { } index
			&& index >= 0
			&& index < journey.Legs.Count)
		{
			return journey.Legs[index].To;
		}

		return journey.From;
	}


	private static Station GetTransferTo(
		Journey journey,
		JourneyTransfer transfer)
	{
		if (transfer.NextLegIndex is { } index
			&& index >= 0
			&& index < journey.Legs.Count)
		{
			return journey.Legs[index].From;
		}

		return journey.To;
	}


	private static DateTimeOffset? GetWalkingTime(
		Station station)
	{
		return null;
	}


	private static bool SameStation(
		Station left,
		Station right)
	{
		return string.Equals(
			left.Id,
			right.Id,
			StringComparison.OrdinalIgnoreCase);
	}
}