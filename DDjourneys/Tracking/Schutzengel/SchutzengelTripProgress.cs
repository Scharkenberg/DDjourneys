using System.Globalization;
using System.Text.Json;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Tracking.Schutzengel;

internal sealed record TripStop(
	string Name,
	DateTimeOffset? Scheduled,
	DateTimeOffset? Realtime,
	double? Latitude,
	double? Longitude,
	string? Platform = null,
	bool PlatformIsTrack = false)
{
	public DateTimeOffset? Effective => Realtime ?? Scheduled;

	public bool HasCoordinates =>
		Latitude.HasValue
		&& Longitude.HasValue
		&& (Latitude != 0 || Longitude != 0);
}

internal sealed record TripPoint(double Latitude, double Longitude);

internal sealed record TripEpisode(
	bool IsIndividual,
	string? Id,
	string? MotName,
	string? Direction,
	TripStop From,
	TripStop To,
	IReadOnlyList<TripStop> Stops,
	IReadOnlyList<TripPoint> Polyline,
	TimeSpan? RequiredTime)
{
	/// <summary>
	/// The walking time a change really needs. A change without a footpath carries its whole
	/// planned wait as "duration"; that is no minimum, so it counts as zero.
	/// </summary>
	public TimeSpan MinimumTransfer
	{
		get
		{
			if (!IsIndividual || RequiredTime is not { } required)
			{
				return TimeSpan.Zero;
			}

			if (From.Scheduled is { } start
				&& To.Scheduled is { } end
				&& Math.Abs((end - start - required).TotalSeconds) <= 5)
			{
				return TimeSpan.Zero;
			}

			return required;
		}
	}
}

/// <summary>A change that is endangered (<see cref="Missed"/> false) or already lost.</summary>
internal sealed record ConnectionRisk(
	int EpisodeIndex,
	bool Missed,
	TimeSpan Slack,
	string Station,
	string? NextLine);

internal enum TripStage
{
	NotStarted,
	Riding,
	Changing,
	Arrived
}

internal sealed record TripSnapshot(
	TripStage Stage,
	TrackingPhase Phase,
	int EpisodeIndex,
	int LegIndex,
	int LegCount,
	IReadOnlyList<int> SegmentLengths,
	IReadOnlyList<bool> SegmentIndividual,
	int Position,
	double Progress,
	string? MotName,
	string? Direction,
	string? CurrentStop,
	string? NextStop,
	DateTimeOffset? NextStopTime,
	string? Origin,
	string? Destination,
	DateTimeOffset? Start,
	DateTimeOffset? Arrival,
	DateTimeOffset? PlannedArrival,
	TimeSpan Delay,
	ConnectionRisk? Risk,
	TripPoint? Location)
{
	public int Length => SegmentLengths.Sum();

	/// <summary>Where the current ride or walk ends (alighting stop for a ride).</summary>
	public TripStop? EpisodeEnd { get; init; }

	/// <summary>Progress position at the start of the current ride or walk (steady within it).</summary>
	public int EpisodeStartPosition { get; init; }

	/// <summary>The ride that follows the current walk: line, boarding stop and time.</summary>
	public TripEpisode? NextRide { get; init; }

	public static TripSnapshot Empty { get; } =
		new(
			TripStage.NotStarted, TrackingPhase.Planned, -1, 0, 0,
			[], [], 0, 0,
			null, null, null, null, null,
			null, null, null, null, null,
			TimeSpan.Zero, null, null);
}

/// <summary>
/// The service's view of a trip: ordered episodes (rides and footpaths) with planned and
/// real-time stop times. Progress is derived from the clock like the reference client does:
/// the current episode is the last one that has started, the stop before the first future stop
/// is the most recent one.
/// </summary>
internal sealed class TripTimeline
{
	private TripTimeline(int? dataVersion, IReadOnlyList<TripEpisode> episodes)
	{
		DataVersion = dataVersion;
		Episodes = episodes;
	}

	public int? DataVersion { get; }

	/// <summary>Per change between consecutive rides: the provider ensures it (the next vehicle waits).</summary>
	private IReadOnlyList<bool> _ensured = [];

	/// <summary>Takes over the ensured changes of the plan's raw data; ignored when they do not fit the rides.</summary>
	public void SetEnsured(IReadOnlyList<bool>? ensured) =>
		_ensured = ensured ?? [];

	private bool IsEnsuredChange(int episodeIndex)
	{
		int rides = Episodes.Count(episode => !episode.IsIndividual);

		if (_ensured.Count == 0 || _ensured.Count != rides - 1)
		{
			return false;
		}

		int before = Episodes.Take(episodeIndex).Count(episode => !episode.IsIndividual) - 1;

		return before >= 0 && before < _ensured.Count && _ensured[before];
	}

	/// <summary>
	/// True when a connection-risk text refers to a change the provider ensures. The provider's guarantee is
	/// authoritative: a service notice (which can only see the clock) must not turn it into a risk. The text is
	/// matched by the change's stop name; when every change of the trip is ensured, any such text qualifies.
	/// </summary>
	public bool CoversEnsuredChange(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		TripEpisode[] rides = [.. Episodes.Where(episode => !episode.IsIndividual)];

		if (rides.Length < 2 || _ensured.Count != rides.Length - 1)
		{
			return false;
		}

		if (_ensured.All(flag => flag))
		{
			return true;
		}

		for (int i = 0; i < _ensured.Count; i++)
		{
			if (_ensured[i]
				&& rides[i].To.Name.Length > 0
				&& text.Contains(rides[i].To.Name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	public IReadOnlyList<TripEpisode> Episodes { get; }

	public DateTimeOffset? Start =>
		Episodes.Count == 0 ? null : Episodes[0].From.Effective;

	public DateTimeOffset? End =>
		Episodes.Count == 0 ? null : Episodes[^1].To.Effective;

	/// <summary>
	/// Reads a <c>planRealtime</c> response, or the <c>journey</c> part of a plan that was posted.
	/// Polylines the service omits in updates are taken over from <paramref name="previous"/>.
	/// </summary>
	public static bool TryParse(JsonElement root, TripTimeline? previous, out TripTimeline timeline)
	{
		timeline = null!;

		if (root.ValueKind == JsonValueKind.Object
			&& !root.TryGetProperty("episodes", out _)
			&& root.TryGetProperty("journey", out JsonElement journey))
		{
			root = journey;
		}

		if (root.ValueKind != JsonValueKind.Object
			|| !root.TryGetProperty("episodes", out JsonElement episodesElement)
			|| episodesElement.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		var episodes = new List<TripEpisode>();
		int index = 0;

		foreach (JsonElement element in episodesElement.EnumerateArray())
		{
			IReadOnlyList<TripPoint>? fallback =
				previous is not null && index < previous.Episodes.Count
					? previous.Episodes[index].Polyline
					: null;

			if (TryParseEpisode(element, fallback, out TripEpisode episode))
			{
				episodes.Add(episode);
			}

			index++;
		}

		if (episodes.Count == 0)
		{
			return false;
		}

		timeline = new TripTimeline(ReadInt(root, "data_version"), Normalize(episodes));
		timeline._ensured = previous?._ensured ?? [];
		return true;
	}

	public TripSnapshot Calculate(DateTimeOffset now)
	{
		if (Episodes.Count == 0)
		{
			return TripSnapshot.Empty;
		}

		int[] lengths = [.. Episodes.Select(SegmentLength)];
		bool[] individual = [.. Episodes.Select(episode => episode.IsIndividual)];
		int totalLength = lengths.Sum();
		int legCount = Math.Max(1, Episodes.Count(episode => !episode.IsIndividual));

		DateTimeOffset? start = Start;
		DateTimeOffset? end = End;
		DateTimeOffset? plannedEnd = Episodes[^1].To.Scheduled ?? end;

		TimeSpan delay =
			end is { } actual && plannedEnd is { } planned
				? actual - planned
				: TimeSpan.Zero;

		string origin = Episodes[0].From.Name;
		string destination = Episodes[^1].To.Name;

		TripSnapshot Build(
			TripStage stage,
			TrackingPhase phase,
			int episodeIndex,
			int legIndex,
			int position,
			string? mot,
			string? direction,
			TripStop? recent,
			TripStop? next,
			ConnectionRisk? risk,
			TripPoint? location) =>
			new(
				stage, phase, episodeIndex, legIndex, legCount,
				lengths, individual, position,
				totalLength == 0 ? 0 : Math.Clamp((double)position / totalLength, 0, 1),
				mot, direction, recent?.Name, next?.Name, next?.Effective,
				origin, destination, start, end, plannedEnd,
				delay, risk, location);

		if (end is { } finish && finish < now)
		{
			TripStop last = Episodes[^1].To;

			return Build(
				TripStage.Arrived, TrackingPhase.Arrived,
				Episodes.Count - 1, legCount - 1, totalLength,
				Episodes[^1].MotName, Episodes[^1].Direction,
				last, null, null,
				last.HasCoordinates
					? new TripPoint(last.Latitude!.Value, last.Longitude!.Value)
					: null);
		}

		int current = -1;

		for (int i = 0; i < Episodes.Count; i++)
		{
			if (Episodes[i].From.Effective is { } episodeStart && episodeStart < now)
			{
				current = i;
			}
		}

		if (current < 0)
		{
			return Build(
				TripStage.NotStarted, TrackingPhase.Planned,
				-1, 0, 0,
				Episodes[0].MotName, Episodes[0].Direction,
				null, Episodes[0].From, FindRisk(now), null);
		}

		TripEpisode episode = Episodes[current];

		int position = 0;

		for (int i = 0; i < current; i++)
		{
			position += lengths[i];
		}

		if (episode.From.Effective is { } currentStart)
		{
			position += (int)Math.Clamp((now - currentStart).TotalSeconds, 0, lengths[current]);
		}

		TripStop recent;
		TripStop? next;

		if (episode.IsIndividual)
		{
			recent = episode.From;
			next = episode.To;
		}
		else
		{
			List<TripStop> timed =
				episode.Stops
					.Where(stop => stop.Effective.HasValue)
					.OrderBy(stop => stop.Effective)
					.ToList();

			int upcoming = timed.FindIndex(stop => stop.Effective > now);

			next = upcoming >= 0 ? timed[upcoming] : null;
			recent =
				upcoming > 0
					? timed[upcoming - 1]
					: upcoming == 0
						? episode.From
						: timed.Count > 0
							? timed[^1]
							: episode.To;
		}

		ConnectionRisk? risk = FindRisk(now);

		TripStage stage = episode.IsIndividual ? TripStage.Changing : TripStage.Riding;

		TrackingPhase phase =
			risk is not null
				? TrackingPhase.AtRisk
				: episode.IsIndividual
					? TrackingPhase.AtInterchange
					: TrackingPhase.InProgress;

		int legIndex =
			Math.Clamp(
				Episodes.Take(current).Count(item => !item.IsIndividual),
				0,
				legCount - 1);

		int episodeStartPosition = lengths.Take(current).Sum();

		TripEpisode? nextRide =
			episode.IsIndividual
				? Episodes.Skip(current + 1).FirstOrDefault(item => !item.IsIndividual)
				: null;

		return Build(
			stage, phase, current, legIndex, position,
			episode.MotName, episode.Direction,
			recent, next, risk,
			episode.IsIndividual
				? null
				: EstimateLocation(now, episode.Polyline, recent, next))
			with
		{
			EpisodeEnd = episode.To,
			EpisodeStartPosition = episodeStartPosition,
			NextRide = nextRide
		};
	}

	/// <summary>
	/// The whole course at <paramref name="now"/>: every episode with every stop and whether it is
	/// behind, current or ahead. Same clock rule as <see cref="Calculate"/>: the current episode is
	/// the last one that has started; in it, the next stop is the first one still in the future.
	/// </summary>
	public TrackedTrip Describe(string planId, DateTimeOffset now)
	{
		bool arrived = End is { } end && end < now;
		int current = -1;

		for (int i = 0; i < Episodes.Count; i++)
		{
			if (Episodes[i].From.Effective is { } episodeStart && episodeStart < now)
			{
				current = i;
			}
		}

		var segments = new List<TrackedSegment>(Episodes.Count);

		for (int i = 0; i < Episodes.Count; i++)
		{
			TripEpisode episode = Episodes[i];

			bool passed = arrived || i < current;
			bool isCurrent = !arrived && i == current;

			IReadOnlyList<TripStop> source =
				episode.IsIndividual || episode.Stops.Count < 2
					? [episode.From, episode.To]
					: episode.Stops;

			int next = -1;

			if (isCurrent)
			{
				for (int j = 0; j < source.Count; j++)
				{
					if (source[j].Effective is { } time && time > now)
					{
						next = j;
						break;
					}
				}
			}

			int recent =
				!isCurrent
					? -1
					: next < 0
						? source.Count - 1
						: next - 1;

			var stops = new List<TrackedStop>(source.Count);

			for (int j = 0; j < source.Count; j++)
			{
				TrackedStopState state =
					passed
						? TrackedStopState.Passed
						: !isCurrent
							? TrackedStopState.Upcoming
							: j == next
								? TrackedStopState.Next
								: j == recent
									? TrackedStopState.Current
									: next < 0 || j < next
										? TrackedStopState.Passed
										: TrackedStopState.Upcoming;

				TripStop stop = source[j];

				stops.Add(new TrackedStop(stop.Name, stop.Scheduled, stop.Realtime, state, stop.Platform, stop.PlatformIsTrack));
			}

			segments.Add(
				new TrackedSegment(
					episode.IsIndividual,
					episode.MotName,
					episode.Direction,
					stops,
					passed,
					isCurrent,
					episode.RequiredTime));
		}

		return new TrackedTrip(planId, segments, now);
	}

	/// <summary>
	/// The earliest change between two rides that has not been left yet and cannot be made:
	/// the next vehicle leaves before the previous one arrives (missed), or earlier than the
	/// footpath allows (at risk).
	/// </summary>
	private ConnectionRisk? FindRisk(DateTimeOffset now)
	{
		for (int i = 1; i < Episodes.Count - 1; i++)
		{
			TripEpisode change = Episodes[i];
			TripEpisode before = Episodes[i - 1];
			TripEpisode after = Episodes[i + 1];

			// An ensured connection waits for the arriving vehicle; it is neither at risk nor lost.
			if (IsEnsuredChange(i))
			{
				continue;
			}

			if (!change.IsIndividual
				|| before.IsIndividual
				|| after.IsIndividual
				|| before.To.Effective is not { } arrival
				|| after.From.Effective is not { } departure
				|| departure <= now)
			{
				continue;
			}

			TimeSpan slack = departure - arrival - change.MinimumTransfer;

			if (slack >= TimeSpan.Zero)
			{
				continue;
			}

			return new ConnectionRisk(i, departure < arrival, slack, change.To.Name, after.MotName);
		}

		return null;
	}

	// ----- Parsing -----

	/// <summary>
	/// A vehicle that is late stays late: stops without a real-time value after a delayed stop of the same ride
	/// inherit that delay (the service often reports real time only for the stops near the vehicle).
	/// Without this a delayed tram counts as arrived at its planned time.
	/// </summary>
	private static TripEpisode CarryDelay(TripEpisode episode)
	{
		if (episode.IsIndividual)
		{
			return episode;
		}

		TimeSpan? delay = null;

		static TimeSpan? DelayOf(TripStop stop) =>
			stop.Realtime is { } real && stop.Scheduled is { } plan ? real - plan : null;

		static TripStop Apply(TripStop stop, TimeSpan? late) =>
			stop.Realtime is null && stop.Scheduled is { } plan && late is { } by && by > TimeSpan.Zero
				? stop with { Realtime = plan + by }
				: stop;

		var stops = new List<TripStop>(episode.Stops.Count);

		foreach (TripStop stop in episode.Stops)
		{
			delay = DelayOf(stop) ?? delay;
			stops.Add(Apply(stop, delay));
		}

		TripStop from = Apply(episode.From, DelayOf(episode.From));
		delay = DelayOf(from) ?? delay;
		TripStop to = Apply(episode.To, delay);

		return episode with { From = from, To = to, Stops = stops };
	}

	private static List<TripEpisode> Normalize(List<TripEpisode> episodes)
	{
		for (int i = 0; i < episodes.Count; i++)
		{
			episodes[i] = CarryDelay(episodes[i]);
		}

		for (int i = 0; i < episodes.Count; i++)
		{
			TripEpisode episode = episodes[i];

			if (!episode.IsIndividual)
			{
				continue;
			}

			TripEpisode? previous = i > 0 ? episodes[i - 1] : null;
			TripEpisode? next = i < episodes.Count - 1 ? episodes[i + 1] : null;

			TripStop fromStop = episode.From;
			TripStop toStop = episode.To;

			if (previous is { IsIndividual: false } && next is { IsIndividual: false })
			{
				// A change between two rides: the vehicles carry the real-time values, the stop names
				// and the platforms (the footpath only knows planned times and may lack the platforms).
				fromStop = fromStop with
				{
					Name = previous.To.Name.Length > 0 ? previous.To.Name : fromStop.Name,
					Platform = previous.To.Platform ?? fromStop.Platform,
					PlatformIsTrack = previous.To.Platform is not null ? previous.To.PlatformIsTrack : fromStop.PlatformIsTrack,
					Scheduled = previous.To.Scheduled ?? fromStop.Scheduled,
					Realtime = previous.To.Realtime
				};

				toStop = toStop with
				{
					Name = next.From.Name.Length > 0 ? next.From.Name : toStop.Name,
					Platform = next.From.Platform ?? toStop.Platform,
					PlatformIsTrack = next.From.Platform is not null ? next.From.PlatformIsTrack : toStop.PlatformIsTrack,
					Scheduled = next.From.Scheduled ?? toStop.Scheduled,
					Realtime = next.From.Realtime
				};
			}
			else
			{
				if (fromStop.Effective is null && previous?.To.Effective is { } arrival)
				{
					fromStop = fromStop with { Scheduled = arrival };
				}

				if (toStop.Effective is null)
				{
					if (next?.From.Effective is { } departure)
					{
						toStop = toStop with { Scheduled = departure };
					}
					else if (fromStop.Effective is { } begin)
					{
						toStop = toStop with { Scheduled = begin + (episode.RequiredTime ?? TimeSpan.Zero) };
					}
				}
			}

			episodes[i] = episode with
			{
				From = fromStop,
				To = toStop,
				Stops = [fromStop, toStop]
			};
		}

		return episodes;
	}

	private static int SegmentLength(TripEpisode episode)
	{
		TimeSpan duration =
			episode.From.Effective is { } start && episode.To.Effective is { } end && end > start
				? end - start
				: episode.RequiredTime ?? TimeSpan.FromMinutes(1);

		return Math.Max(30, (int)Math.Round(duration.TotalSeconds));
	}

	private static bool TryParseEpisode(
		JsonElement element,
		IReadOnlyList<TripPoint>? fallbackPolyline,
		out TripEpisode episode)
	{
		episode = null!;

		if (element.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		bool individual =
			string.Equals(
				SchutzengelPlanList.ReadString(element, "type"),
				"individual",
				StringComparison.OrdinalIgnoreCase);

		string? motName = null;
		string? direction = null;

		if (element.TryGetProperty("mot", out JsonElement mot) && mot.ValueKind == JsonValueKind.Object)
		{
			motName = SchutzengelPlanList.ReadString(mot, "name");
			direction = SchutzengelPlanList.ReadString(mot, "direction");
		}

		var stops = new List<TripStop>();

		if (element.TryGetProperty("allStations", out JsonElement all) && all.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in all.EnumerateArray())
			{
				if (TryParseStop(item, out TripStop stop))
				{
					stops.Add(stop);
				}
			}
		}

		TripStop? fromStop = TryParseStop(element, "from");
		TripStop? toStop = TryParseStop(element, "to");

		if (stops.Count == 0)
		{
			if (fromStop is not null)
			{
				stops.Add(fromStop);
			}

			if (toStop is not null && (stops.Count == 0 || toStop != stops[^1]))
			{
				stops.Add(toStop);
			}
		}

		fromStop ??= stops.FirstOrDefault();
		toStop ??= stops.LastOrDefault();

		if (fromStop is null || toStop is null)
		{
			return false;
		}

		List<TripPoint> polyline = ParsePolyline(element);

		if (polyline.Count == 0 && fallbackPolyline is not null)
		{
			polyline = [.. fallbackPolyline];
		}

		TimeSpan? required = null;

		if (ReadDouble(element, "durationSeconds") is { } seconds && seconds >= 0)
		{
			required = TimeSpan.FromSeconds(seconds);
		}
		else if (ReadDouble(element, "requiredTimeMS") is { } milliseconds && milliseconds >= 0)
		{
			required = TimeSpan.FromMilliseconds(milliseconds);
		}

		episode = new TripEpisode(
			individual,
			SchutzengelPlanList.ReadString(element, "id"),
			motName,
			direction,
			fromStop,
			toStop,
			stops,
			polyline,
			required);

		return true;
	}

	private static TripStop? TryParseStop(JsonElement parent, string name) =>
		parent.TryGetProperty(name, out JsonElement stop) && TryParseStop(stop, out TripStop parsed)
			? parsed
			: null;

	private static bool TryParseStop(JsonElement element, out TripStop stop)
	{
		stop = null!;

		if (element.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		element.TryGetProperty("scheduledTime", out JsonElement scheduled);
		element.TryGetProperty("realtime", out JsonElement realtime);

		double? latitude = null;
		double? longitude = null;

		if (element.TryGetProperty("coords", out JsonElement coords) && coords.ValueKind == JsonValueKind.Object)
		{
			latitude = ReadDouble(coords, "lat");
			longitude = ReadDouble(coords, "lon");
		}

		(string? platform, bool isTrack) = ReadPlatform(element);

		stop = new TripStop(
			SchutzengelPlanList.ReadString(element, "name") ?? string.Empty,
			SchutzengelTime.Read(scheduled),
			SchutzengelTime.Read(realtime),
			latitude ?? ReadDouble(element, "lat"),
			longitude ?? ReadDouble(element, "lon"),
			platform,
			isTrack);

		return true;
	}

	/// <summary>
	/// The platform of a stop: an object {type, name} ("Steig" = platform, "Gleis"/"Railtrack" = track,
	/// or the numeric wire code 2 for a track) or, leniently, a plain string.
	/// </summary>
	private static (string? Name, bool IsTrack) ReadPlatform(JsonElement stop)
	{
		if (!stop.TryGetProperty("platform", out JsonElement platform))
		{
			return (null, false);
		}

		if (platform.ValueKind == JsonValueKind.String)
		{
			return (Clean(platform.GetString()), false);
		}

		if (platform.ValueKind != JsonValueKind.Object)
		{
			return (null, false);
		}

		string? name = Clean(SchutzengelPlanList.ReadString(platform, "name"));
		bool isTrack = false;

		if (platform.TryGetProperty("type", out JsonElement type))
		{
			isTrack =
				type.ValueKind == JsonValueKind.Number
					? type.TryGetInt32(out int code) && code == SchutzengelWireCodes.PlatformTypeRailtrack
					: type.ValueKind == JsonValueKind.String
						&& type.GetString() is { } text
						&& (text.Contains("gleis", StringComparison.OrdinalIgnoreCase)
							|| text.Contains("rail", StringComparison.OrdinalIgnoreCase)
							|| text.Contains("track", StringComparison.OrdinalIgnoreCase));
		}

		return (name, isTrack);
	}

	private static string? Clean(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static List<TripPoint> ParsePolyline(JsonElement element)
	{
		var result = new List<TripPoint>();

		if (!element.TryGetProperty("polyline", out JsonElement polyline)
			|| polyline.ValueKind != JsonValueKind.Array)
		{
			return result;
		}

		foreach (JsonElement point in polyline.EnumerateArray())
		{
			if (point.ValueKind == JsonValueKind.Object
				&& ReadDouble(point, "lat") is { } latitude
				&& ReadDouble(point, "lon") is { } longitude)
			{
				result.Add(new TripPoint(latitude, longitude));
			}
		}

		return result;
	}

	// ----- Position -----

	/// <summary>
	/// Linear between the last and the next stop, then snapped to the nearest point of the
	/// route line (the reference client does the same).
	/// </summary>
	private static TripPoint? EstimateLocation(
		DateTimeOffset now,
		IReadOnlyList<TripPoint> polyline,
		TripStop? recent,
		TripStop? next)
	{
		if (recent is not { HasCoordinates: true }
			|| next is not { HasCoordinates: true }
			|| recent.Effective is not { } recentTime
			|| next.Effective is not { } nextTime
			|| nextTime <= recentTime)
		{
			return null;
		}

		double ratio =
			Math.Clamp(
				(now - recentTime).TotalMilliseconds / (nextTime - recentTime).TotalMilliseconds,
				0,
				1);

		var linear = new TripPoint(
			recent.Latitude!.Value * (1 - ratio) + next.Latitude!.Value * ratio,
			recent.Longitude!.Value * (1 - ratio) + next.Longitude!.Value * ratio);

		if (polyline.Count < 2)
		{
			return linear;
		}

		TripPoint best = linear;
		double bestDistance = double.MaxValue;

		for (int i = 1; i < polyline.Count; i++)
		{
			TripPoint a = polyline[i - 1];
			TripPoint b = polyline[i];

			double dx = b.Latitude - a.Latitude;
			double dy = b.Longitude - a.Longitude;
			double squared = dx * dx + dy * dy;

			double t =
				squared <= double.Epsilon
					? 0
					: Math.Clamp(
						((linear.Latitude - a.Latitude) * dx + (linear.Longitude - a.Longitude) * dy) / squared,
						0,
						1);

			double latitude = a.Latitude + t * dx;
			double longitude = a.Longitude + t * dy;

			double distance =
				Math.Pow(linear.Latitude - latitude, 2)
				+ Math.Pow(linear.Longitude - longitude, 2);

			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = new TripPoint(latitude, longitude);
			}
		}

		return best;
	}

	// ----- JSON helpers -----

	private static double? ReadDouble(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out JsonElement value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
		{
			return number;
		}

		if (value.ValueKind == JsonValueKind.String
			&& double.TryParse(
				value.GetString(),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out double parsed))
		{
			return parsed;
		}

		return null;
	}

	private static int? ReadInt(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out JsonElement value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
		{
			return number;
		}

		if (value.ValueKind == JsonValueKind.String
			&& int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
		{
			return parsed;
		}

		return null;
	}
}