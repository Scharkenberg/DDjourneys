using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelTripTimeline
{
	public int? DataVersion { get; }

	public IReadOnlyList<SchutzengelTripEpisode> Episodes { get; }


	private SchutzengelTripTimeline(
		int? dataVersion,
		IReadOnlyList<SchutzengelTripEpisode> episodes)
	{
		DataVersion = dataVersion;
		Episodes = episodes;
	}


	public static SchutzengelTripTimeline FromJourney(
		Journey journey)
	{
		ArgumentNullException.ThrowIfNull(
			journey);


		var episodes =
			new List<SchutzengelTripEpisode>();


		for (int i = 0;
			i < journey.Legs.Count;
			i++)
		{
			JourneyLeg leg =
				journey.Legs[i];


			episodes.Add(
				CreateJourneyLegEpisode(
					leg,
					i));


			if (i >= journey.Legs.Count - 1)
			{
				continue;
			}


			JourneyLeg nextLeg =
				journey.Legs[i + 1];


			JourneyTransfer? transfer =
				journey.Transfers.FirstOrDefault(
					item =>
						item.PreviousLegIndex == i
						&& item.NextLegIndex == i + 1);


			if (transfer is not null)
			{
				episodes.Add(
					CreateTransferEpisode(
						transfer,
						leg,
						nextLeg,
						i));

				continue;
			}


			DateTimeOffset? previousArrival =
				leg.EffectiveArrival;

			DateTimeOffset? nextDeparture =
				nextLeg.EffectiveDeparture;


			if (previousArrival is null
				|| nextDeparture is null
				|| nextDeparture <= previousArrival
				|| leg.To.Id != nextLeg.From.Id)
			{
				continue;
			}


			var syntheticTransfer =
				new JourneyTransfer
				{
					Location =
						leg.To,

					PreviousLegIndex =
						i,

					NextLegIndex =
						i + 1,

					Duration =
						nextDeparture.Value - previousArrival.Value,

					WaitingTime =
						nextDeparture.Value - previousArrival.Value,

					Kind =
						TransferKind.Walk,

					IsGuaranteed =
						true,

					From =
						leg.To,

					To =
						nextLeg.From,

					Path =
						[]
				};


			episodes.Add(
				CreateTransferEpisode(
					syntheticTransfer,
					leg,
					nextLeg,
					i));
		}


		return new SchutzengelTripTimeline(
			null,
			episodes);
	}


	public static bool TryParseRealtime(
		JsonElement root,
		Journey journey,
		out SchutzengelTripTimeline timeline)
	{
		timeline =
			null!;


		if (root.ValueKind !=
			JsonValueKind.Object
			|| !root.TryGetProperty(
				"episodes",
				out JsonElement episodesElement)
			|| episodesElement.ValueKind !=
				JsonValueKind.Array)
		{
			return false;
		}


		var episodes =
			new List<SchutzengelTripEpisode>();


		Dictionary<string, int> legIds =
			journey.Legs
				.Select(
					(leg, index) =>
						(
							leg.Id,
							index))
				.Where(
					item =>
						!string.IsNullOrWhiteSpace(
							item.Id))
				.GroupBy(
					item =>
						item.Id!,
					StringComparer.Ordinal)
				.ToDictionary(
					group =>
						group.Key,
					group =>
						group.First().index,
					StringComparer.Ordinal);


		int inferredMovementIndex =
			0;


		foreach (JsonElement episodeElement in
			episodesElement.EnumerateArray())
		{
			if (!TryParseEpisode(
				episodeElement,
				out SchutzengelTripEpisode episode))
			{
				continue;
			}


			int legIndex;


			if (!string.IsNullOrWhiteSpace(
				episode.Id)
				&& legIds.TryGetValue(
					episode.Id,
					out int mappedIndex))
			{
				legIndex =
					mappedIndex;
			}
			else if (
				episode.IsIndividual
				&& string.IsNullOrWhiteSpace(
					episode.Id))
			{
				legIndex =
					Math.Max(
						0,
						inferredMovementIndex - 1);
			}
			else
			{
				legIndex =
					Math.Min(
						inferredMovementIndex,
						Math.Max(
							0,
							journey.Legs.Count - 1));

				inferredMovementIndex++;
			}


			episodes.Add(
				episode with
				{
					LegIndex =
						legIndex
				});
		}


		if (episodes.Count == 0)
		{
			return false;
		}


		episodes =
			episodes
				.OrderBy(
					episode =>
						episode.From?.EffectiveTime
						?? DateTimeOffset.MaxValue)
				.ToList();


		int? dataVersion =
			ReadInt(
				root,
				"data_version");


		timeline =
			new SchutzengelTripTimeline(
				dataVersion,
				episodes);


		return true;
	}


	public SchutzengelProgressSnapshot Calculate(
		DateTimeOffset now)
	{
		if (Episodes.Count == 0)
		{
			return new SchutzengelProgressSnapshot(
				CurrentEpisodeIndex: -1,
				CurrentLegIndex: 0,
				LegCount: 0,
				TimeProgress: 0,
				CurrentEpisodeProgress: -1,
				Phase: TrackingPhase.Planned,
				MotName: null,
				CurrentStopName: null,
				NextStopName: null,
				EstimatedArrival: null,
				Message: "Waiting for journey data",
				EstimatedLocation: null);
		}


		DateTimeOffset? firstStart =
			Episodes
				.First()
				.From?
				.EffectiveTime;


		DateTimeOffset? finalEnd =
			Episodes
				.Last()
				.To?
				.EffectiveTime;


		int currentEpisodeIndex =
			-1;


		for (int i = 0;
			i < Episodes.Count;
			i++)
		{
			DateTimeOffset? start =
				Episodes[i]
					.From?
					.EffectiveTime;


			if (start is { } value
				&& value < now)
			{
				currentEpisodeIndex =
					i;
			}
		}


		bool finished =
			finalEnd is { } end
			&& end < now;


		double timeProgress =
			CalculateRatio(
				now,
				firstStart,
				finalEnd);


		if (finished)
		{
			SchutzengelTripEpisode finalEpisode =
				Episodes[^1];


			string finalMessage =
				finalEpisode.To?.Name is { Length: > 0 } destination
					? $"Arrived at {destination}"
					: "Arrived";


			return new SchutzengelProgressSnapshot(
				CurrentEpisodeIndex:
					Episodes.Count - 1,

				CurrentLegIndex:
					Math.Clamp(
						finalEpisode.LegIndex,
						0,
						Math.Max(
							0,
							Episodes
								.Max(
									episode =>
										episode.LegIndex))),

				LegCount:
					Math.Max(
						1,
						Episodes.Max(
							episode =>
								episode.LegIndex + 1)),

				TimeProgress:
					1,

				CurrentEpisodeProgress:
					1,

				Phase:
					TrackingPhase.Arrived,

				MotName:
					finalEpisode.MotName,

				CurrentStopName:
					finalEpisode.To?.Name,

				NextStopName:
					null,

				EstimatedArrival:
					finalEnd,

				Message:
					finalMessage,

				EstimatedLocation:
					finalEpisode.To?.HasCoordinates == true
						? new SchutzengelGeoPoint(
							finalEpisode.To.Latitude!.Value,
							finalEpisode.To.Longitude!.Value)
						: null);
		}


		if (currentEpisodeIndex < 0)
		{
			SchutzengelTripEpisode firstEpisode =
				Episodes[0];


			string startMessage =
				firstEpisode.From?.Name is { Length: > 0 } origin
					&& firstStart is { } start
						? $"Starts {start.ToLocalTime():t} · {origin}"
						: "Journey planned";


			return new SchutzengelProgressSnapshot(
				CurrentEpisodeIndex:
					-1,

				CurrentLegIndex:
					0,

				LegCount:
					Math.Max(
						1,
						Episodes.Max(
							episode =>
								episode.LegIndex + 1)),

				TimeProgress:
					0,

				CurrentEpisodeProgress:
					-1,

				Phase:
					TrackingPhase.Planned,

				MotName:
					firstEpisode.MotName,

				CurrentStopName:
					null,

				NextStopName:
					firstEpisode.From?.Name,

				EstimatedArrival:
					finalEnd,

				Message:
					startMessage,

				EstimatedLocation:
					null);
		}


		SchutzengelTripEpisode currentEpisode =
			Episodes[currentEpisodeIndex];


		List<SchutzengelTripStop> timedStops =
			currentEpisode
				.AllStations
				.Where(
					stop =>
						stop.EffectiveTime.HasValue)
				.OrderBy(
					stop =>
						stop.EffectiveTime)
				.ToList();


		SchutzengelTripStop? recentStop =
			null;

		SchutzengelTripStop? nextStop =
			null;


		for (int i = 0;
			i < timedStops.Count;
			i++)
		{
			if (timedStops[i]
					.EffectiveTime!.Value > now)
			{
				nextStop =
					timedStops[i];

				recentStop =
					i > 0
						? timedStops[i - 1]
						: currentEpisode.From;

				break;
			}
		}


		recentStop ??=
			currentEpisode.To
			?? currentEpisode.From;


		double episodeProgress =
			CalculateRatio(
				now,
				currentEpisode.From?.EffectiveTime,
				currentEpisode.To?.EffectiveTime);


		TrackingPhase phase =
			currentEpisode.IsIndividual
				? TrackingPhase.AtInterchange
				: TrackingPhase.InProgress;


		string message;


		if (currentEpisode.IsIndividual)
		{
			string transferName =
				nextStop?.Name
				?? currentEpisode.To?.Name
				?? recentStop?.Name
				?? "transfer";


			message =
				$"Changing · {transferName}";
		}
		else if (nextStop?.Name is { Length: > 0 } next)
		{
			message =
				string.IsNullOrWhiteSpace(
					currentEpisode.MotName)
					? $"Next: {next}"
					: $"{currentEpisode.MotName} · Next: {next}";
		}
		else if (recentStop?.Name is { Length: > 0 } recent)
		{
			message =
				string.IsNullOrWhiteSpace(
					currentEpisode.MotName)
					? recent
					: $"{currentEpisode.MotName} · {recent}";
		}
		else
		{
			message =
				currentEpisode.MotName
				?? "Journey in progress";
		}


		SchutzengelGeoPoint? estimatedLocation =
			CalculateLocation(
				now,
				currentEpisode.Polyline,
				recentStop,
				nextStop);


		return new SchutzengelProgressSnapshot(
			CurrentEpisodeIndex:
				currentEpisodeIndex,

			CurrentLegIndex:
				Math.Max(
					0,
					currentEpisode.LegIndex),

			LegCount:
				Math.Max(
					1,
					Episodes.Max(
						episode =>
							episode.LegIndex + 1)),

			TimeProgress:
				timeProgress,

			CurrentEpisodeProgress:
				episodeProgress,

			Phase:
				phase,

			MotName:
				currentEpisode.MotName,

			CurrentStopName:
				recentStop?.Name,

			NextStopName:
				nextStop?.Name,

			EstimatedArrival:
				finalEnd,

			Message:
				message,

			EstimatedLocation:
				estimatedLocation);
	}


	private static SchutzengelTripEpisode CreateJourneyLegEpisode(
		JourneyLeg leg,
		int legIndex)
	{
		List<SchutzengelTripStop> stops =
			leg.Stops
				.Select(
					ToProgressStop)
				.ToList();


		if (stops.Count == 0)
		{
			stops.Add(
				new SchutzengelTripStop(
					leg.From.Name,
					leg.ScheduledDeparture,
					leg.RealtimeDeparture,
					leg.From.Latitude,
					leg.From.Longitude));

			stops.Add(
				new SchutzengelTripStop(
					leg.To.Name,
					leg.ScheduledArrival,
					leg.RealtimeArrival,
					leg.To.Latitude,
					leg.To.Longitude));
		}


		string type =
			leg.Mode == TransitMode.Walk
				? "individual"
				: "public";


		string? motName =
			leg.Mode == TransitMode.Walk
				? "Walking"
				: leg.Line?.Name
					?? leg.Mode.ToString();


		return new SchutzengelTripEpisode(
			Type:
				type,

			LegIndex:
				legIndex,

			Id:
				leg.Id,

			MotName:
				motName,

			From:
				stops.FirstOrDefault(),

			To:
				stops.LastOrDefault(),

			AllStations:
				stops,

			Polyline:
				leg.Path
					.Select(
						point =>
							new SchutzengelGeoPoint(
								point.Latitude,
								point.Longitude))
					.ToList());
	}


	private static SchutzengelTripEpisode CreateTransferEpisode(
		JourneyTransfer transfer,
		JourneyLeg previousLeg,
		JourneyLeg nextLeg,
		int previousLegIndex)
	{
		SchutzengelTripStop from =
			new(
				transfer.From?.Name
					?? previousLeg.To.Name,

				previousLeg.EffectiveArrival,

				previousLeg.EffectiveArrival,

				(transfer.From ?? previousLeg.To).Latitude,

				(transfer.From ?? previousLeg.To).Longitude);


		SchutzengelTripStop to =
			new(
				transfer.To?.Name
					?? nextLeg.From.Name,

				nextLeg.EffectiveDeparture,

				nextLeg.EffectiveDeparture,

				(transfer.To ?? nextLeg.From).Latitude,

				(transfer.To ?? nextLeg.From).Longitude);


		if (from.EffectiveTime is { } fromTime
			&& to.EffectiveTime is { } toTime
			&& toTime < fromTime
			&& transfer.Duration > TimeSpan.Zero)
		{
			to =
				to with
				{
					ScheduledTime =
						fromTime + transfer.Duration
				};
		}


		return new SchutzengelTripEpisode(
			Type:
				"individual",

			LegIndex:
				previousLegIndex,

			Id:
				string.Empty,

			MotName:
				"Walking",

			From:
				from,

			To:
				to,

			AllStations:
			[
				from,
				to
			],

			Polyline:
				transfer.Path
					.Select(
						point =>
							new SchutzengelGeoPoint(
								point.Latitude,
								point.Longitude))
					.ToList());
	}


	private static SchutzengelTripStop ToProgressStop(
		StopTime stop)
	{
		return new SchutzengelTripStop(
			stop.Station.Name,

			stop.ScheduledDeparture
				?? stop.ScheduledArrival,

			stop.RealtimeDeparture
				?? stop.RealtimeArrival,

			stop.Station.Latitude,
			stop.Station.Longitude);
	}


	private static bool TryParseEpisode(
		JsonElement element,
		out SchutzengelTripEpisode episode)
	{
		episode =
			null!;


		if (element.ValueKind !=
			JsonValueKind.Object)
		{
			return false;
		}


		string type =
			ReadString(
				element,
				"type")
			?? "public";


		string? id =
			ReadString(
				element,
				"id");


		string? motName =
			element.TryGetProperty(
				"mot",
				out JsonElement mot)
				&& mot.ValueKind ==
					JsonValueKind.Object
					? ReadString(
						mot,
						"name")
					: null;


		var stations =
			new List<SchutzengelTripStop>();


		if (element.TryGetProperty(
			"allStations",
			out JsonElement allStations)
			&& allStations.ValueKind ==
				JsonValueKind.Array)
		{
			foreach (JsonElement stationElement in
				allStations.EnumerateArray())
			{
				if (TryParseStop(
					stationElement,
					out SchutzengelTripStop stop))
				{
					stations.Add(
						stop);
				}
			}
		}


		SchutzengelTripStop? from =
			element.TryGetProperty(
				"from",
				out JsonElement fromElement)
				&& TryParseStop(
					fromElement,
					out SchutzengelTripStop parsedFrom)
						? parsedFrom
						: null;


		SchutzengelTripStop? to =
			element.TryGetProperty(
				"to",
				out JsonElement toElement)
				&& TryParseStop(
					toElement,
					out SchutzengelTripStop parsedTo)
						? parsedTo
						: null;


		if (stations.Count == 0)
		{
			if (from is not null)
			{
				stations.Add(
					from);
			}

			if (to is not null
				&& (
					stations.Count == 0
					|| to != stations[^1]))
			{
				stations.Add(
					to);
			}
		}


		from ??=
			stations.FirstOrDefault();


		to ??=
			stations.LastOrDefault();


		if (from is null
			|| to is null)
		{
			return false;
		}


		List<SchutzengelGeoPoint> polyline =
			ParsePolyline(
				element);


		episode =
			new SchutzengelTripEpisode(
				Type:
					type,

				LegIndex:
					0,

				Id:
					id,

				MotName:
					motName,

				From:
					from,

				To:
					to,

				AllStations:
					stations,

				Polyline:
					polyline);


		return true;
	}


	private static bool TryParseStop(
		JsonElement element,
		out SchutzengelTripStop stop)
	{
		stop =
			null!;


		if (element.ValueKind !=
			JsonValueKind.Object)
		{
			return false;
		}


		string name =
			ReadString(
				element,
				"name")
			?? string.Empty;


		DateTimeOffset? scheduled =
			ReadTimestamp(
				element,
				"scheduledTime");


		DateTimeOffset? realtime =
			ReadTimestamp(
				element,
				"realtime");


		double? latitude = null;
		double? longitude = null;


		if (element.TryGetProperty(
			"coords",
			out JsonElement coords)
			&& coords.ValueKind ==
				JsonValueKind.Object)
		{
			latitude =
				ReadDouble(
					coords,
					"lat");

			longitude =
				ReadDouble(
					coords,
					"lon");
		}


		if (latitude is null)
		{
			latitude =
				ReadDouble(
					element,
					"lat");
		}


		if (longitude is null)
		{
			longitude =
				ReadDouble(
					element,
					"lon");
		}


		stop =
			new SchutzengelTripStop(
				name,
				scheduled,
				realtime,
				latitude,
				longitude);


		return true;
	}


	private static List<SchutzengelGeoPoint> ParsePolyline(
		JsonElement element)
	{
		var result =
			new List<SchutzengelGeoPoint>();


		if (!element.TryGetProperty(
			"polyline",
			out JsonElement polyline)
			|| polyline.ValueKind !=
				JsonValueKind.Array)
		{
			return result;
		}


		foreach (JsonElement point in
			polyline.EnumerateArray())
		{
			if (point.ValueKind !=
				JsonValueKind.Object)
			{
				continue;
			}


			double? latitude =
				ReadDouble(
					point,
					"lat");


			double? longitude =
				ReadDouble(
					point,
					"lon");


			if (latitude is not null
				&& longitude is not null)
			{
				result.Add(
					new SchutzengelGeoPoint(
						latitude.Value,
						longitude.Value));
			}
		}


		return result;
	}


	private static SchutzengelGeoPoint? CalculateLocation(
		DateTimeOffset now,
		IReadOnlyList<SchutzengelGeoPoint> polyline,
		SchutzengelTripStop? recentStop,
		SchutzengelTripStop? nextStop)
	{
		if (recentStop is null
			|| nextStop is null
			|| !recentStop.HasCoordinates
			|| !nextStop.HasCoordinates
			|| recentStop.EffectiveTime is not { } recentTime
			|| nextStop.EffectiveTime is not { } nextTime
			|| nextTime <= recentTime)
		{
			return null;
		}


		double ratio =
			(now - recentTime).TotalMilliseconds
			/
			(nextTime - recentTime).TotalMilliseconds;


		ratio =
			Math.Clamp(
				ratio,
				0,
				1);


		double latitude =
			recentStop.Latitude!.Value
			* (1 - ratio)
			+
			nextStop.Latitude!.Value
			* ratio;


		double longitude =
			recentStop.Longitude!.Value
			* (1 - ratio)
			+
			nextStop.Longitude!.Value
			* ratio;


		var interpolated =
			new SchutzengelGeoPoint(
				latitude,
				longitude);


		if (polyline.Count < 2)
		{
			return interpolated;
		}


		double bestDistance =
			double.MaxValue;


		SchutzengelGeoPoint bestPoint =
			interpolated;


		for (int i = 1;
			i < polyline.Count;
			i++)
		{
			SchutzengelGeoPoint a =
				polyline[i - 1];

			SchutzengelGeoPoint b =
				polyline[i];


			double dx =
				b.Latitude - a.Latitude;

			double dy =
				b.Longitude - a.Longitude;


			double denominator =
				dx * dx
				+
				dy * dy;


			double t =
				denominator <=
					double.Epsilon
					? 0
					: (
						(interpolated.Latitude - a.Latitude)
							* dx
						+
						(interpolated.Longitude - a.Longitude)
							* dy)
						/ denominator;


			t =
				Math.Clamp(
					t,
					0,
					1);


			double projectedLatitude =
				a.Latitude
				+
				t * dx;

			double projectedLongitude =
				a.Longitude
				+
				t * dy;


			double distance =
				DistanceSquared(
					interpolated.Latitude,
					interpolated.Longitude,
					projectedLatitude,
					projectedLongitude);


			if (distance <
				bestDistance)
			{
				bestDistance =
					distance;

				bestPoint =
					new SchutzengelGeoPoint(
						projectedLatitude,
						projectedLongitude);
			}
		}


		return bestPoint;
	}


	private static double CalculateRatio(
		DateTimeOffset now,
		DateTimeOffset? start,
		DateTimeOffset? end)
	{
		if (start is null
			|| end is null)
		{
			return 0;
		}


		if (end <= start)
		{
			return now >= end
				? 1
				: 0;
		}


		if (now <= start)
		{
			return 0;
		}


		if (now >= end)
		{
			return 1;
		}


		return
			(now - start.Value).TotalMilliseconds
			/
			(end.Value - start.Value).TotalMilliseconds;
	}


	private static double DistanceSquared(
		double latitude1,
		double longitude1,
		double latitude2,
		double longitude2)
	{
		double dLatitude =
			latitude1 - latitude2;

		double dLongitude =
			longitude1 - longitude2;


		return
			dLatitude * dLatitude
			+
			dLongitude * dLongitude;
	}


	private static string? ReadString(
		JsonElement objectElement,
		string propertyName)
	{
		return objectElement.TryGetProperty(
			propertyName,
			out JsonElement value)
			&& value.ValueKind ==
				JsonValueKind.String
			? value.GetString()
			: null;
	}


	private static double? ReadDouble(
		JsonElement objectElement,
		string propertyName)
	{
		if (!objectElement.TryGetProperty(
			propertyName,
			out JsonElement value))
		{
			return null;
		}


		if (value.ValueKind ==
			JsonValueKind.Number
			&& value.TryGetDouble(
				out double number))
		{
			return number;
		}


		if (value.ValueKind ==
			JsonValueKind.String
			&& double.TryParse(
				value.GetString(),
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out double parsed))
		{
			return parsed;
		}


		return null;
	}


	private static int? ReadInt(
		JsonElement objectElement,
		string propertyName)
	{
		if (!objectElement.TryGetProperty(
			propertyName,
			out JsonElement value))
		{
			return null;
		}


		if (value.ValueKind ==
			JsonValueKind.Number
			&& value.TryGetInt32(
				out int number))
		{
			return number;
		}


		if (value.ValueKind ==
			JsonValueKind.String
			&& int.TryParse(
				value.GetString(),
				out int parsed))
		{
			return parsed;
		}


		return null;
	}


	private static DateTimeOffset? ReadTimestamp(
		JsonElement objectElement,
		string propertyName)
	{
		if (!objectElement.TryGetProperty(
			propertyName,
			out JsonElement value))
		{
			return null;
		}


		if (value.ValueKind ==
			JsonValueKind.Number)
		{
			if (value.TryGetInt64(
				out long epoch))
			{
				return DateTimeOffset.FromUnixTimeMilliseconds(
					Math.Abs(epoch) > 100_000_000_000
						? epoch
						: epoch * 1000);
			}


			if (value.TryGetDouble(
				out double epochDouble))
			{
				long milliseconds =
					(long)epochDouble;


				return DateTimeOffset.FromUnixTimeMilliseconds(
					Math.Abs(milliseconds) > 100_000_000_000
						? milliseconds
						: milliseconds * 1000);
			}
		}


		if (value.ValueKind ==
			JsonValueKind.String)
		{
			string? text =
				value.GetString();


			if (long.TryParse(
				text,
				out long epoch))
			{
				return DateTimeOffset.FromUnixTimeMilliseconds(
					Math.Abs(epoch) > 100_000_000_000
						? epoch
						: epoch * 1000);
			}


			if (DateTimeOffset.TryParse(
				text,
				out DateTimeOffset parsed))
			{
				return parsed;
			}
		}


		return null;
	}
}


internal sealed record SchutzengelTripEpisode(
	string Type,
	int LegIndex,
	string? Id,
	string? MotName,
	SchutzengelTripStop? From,
	SchutzengelTripStop? To,
	IReadOnlyList<SchutzengelTripStop> AllStations,
	IReadOnlyList<SchutzengelGeoPoint> Polyline)
{
	public bool IsIndividual =>
		string.Equals(
			Type,
			"individual",
			StringComparison.OrdinalIgnoreCase);
}


internal sealed record SchutzengelTripStop(
	string Name,
	DateTimeOffset? ScheduledTime,
	DateTimeOffset? RealtimeTime,
	double? Latitude,
	double? Longitude)
{
	public DateTimeOffset? EffectiveTime =>
		RealtimeTime
		?? ScheduledTime;


	public bool HasCoordinates =>
		Latitude.HasValue
		&& Longitude.HasValue;
}


internal sealed record SchutzengelGeoPoint(
	double Latitude,
	double Longitude);


internal sealed record SchutzengelProgressSnapshot(
	int CurrentEpisodeIndex,
	int CurrentLegIndex,
	int LegCount,
	double TimeProgress,
	double CurrentEpisodeProgress,
	TrackingPhase Phase,
	string? MotName,
	string? CurrentStopName,
	string? NextStopName,
	DateTimeOffset? EstimatedArrival,
	string? Message,
	SchutzengelGeoPoint? EstimatedLocation);