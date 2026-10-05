using System.Globalization;
using DDjourneys.Core.Mapping;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support;

/// <summary>Builds the maps of the app (journey, run, live vehicles, stops) and opens the map page.</summary>
public static class MapScenes
{
	// The map uses the app's own colours: the mode tokens of the chips and the active theme's palette.
	private static string WalkColor => Hex(ModeColors.For(TransitMode.Walk));

	private static string AccentColor =>
		Hex(Theme.ColorOf("Accent", Color.FromArgb("#0B6E8A")));

	private static string Hex(Color color) =>
		$"#{(int)Math.Round(color.Red * 255):X2}{(int)Math.Round(color.Green * 255):X2}{(int)Math.Round(color.Blue * 255):X2}";

	private static ExtrasStrings Strings =>
		LocalizationService.Current.CurrentStrings.Extras;

	/// <summary>Opens the map page with a scene; says so (returns false) when there is nothing to show.</summary>
	public static async Task<bool> OpenAsync(
		MapScene scene,
		string title)
	{
		ArgumentNullException.ThrowIfNull(scene);

		if (scene.IsEmpty)
		{
			return false;
		}

		await Shell.Current.GoToAsync(
			Routes.Map,
			new ShellNavigationQueryParameters
			{
				[Routes.MapScene] = scene,
				[Routes.MapTitle] = title
			});

		return true;
	}

	public static string ModeColor(TransitMode mode) =>
		Hex(ModeColors.For(mode));

	private static (double Latitude, double Longitude)? Position(
		double? latitude,
		double? longitude) =>
		latitude is { } lat
		&& longitude is { } lon
		&& !(lat == 0 && lon == 0)
			? (lat, lon)
			: null;

	private static (double Latitude, double Longitude)? Position(Station? station) =>
		station is null
			? null
			: Position(station.Latitude, station.Longitude);

	private static (double Latitude, double Longitude)? FirstOf(
		IReadOnlyList<(double Latitude, double Longitude)> points) =>
		points.Count > 0
			? points[0]
			: null;

	private static (double Latitude, double Longitude)? LastOf(
		IReadOnlyList<(double Latitude, double Longitude)> points) =>
		points.Count > 0
			? points[^1]
			: null;

	private static IReadOnlyList<(double Latitude, double Longitude)> LegPoints(JourneyLeg leg)
	{
		if (leg.Path.Count >= 2)
		{
			return leg.Path;
		}

		(double Latitude, double Longitude)[] stops =
			[.. leg.Stops
				.Select(stop => Position(stop.Station))
				.OfType<(double Latitude, double Longitude)>()];

		if (stops.Length >= 2)
		{
			return stops;
		}

		return
			(List<(double Latitude, double Longitude)>)
			[.. new[] { Position(leg.From), Position(leg.To) }
				.OfType<(double Latitude, double Longitude)>()];
	}

	// ----- Journey -----

	/// <summary>
	/// The place the start (or end) marker belongs at. When the passenger starts at a stop, that is the stop and
	/// platform the provider names for the first (last) leg, not the stop's centre point the search returned;
	/// when it is an address or a point of interest, it is that place and the walk leads from there.
	/// </summary>
	private static (double Latitude, double Longitude)? Terminal(
		Station? searched,
		Station? legStation,
		(double Latitude, double Longitude)? fallback)
	{
		bool isStop =
			searched is null
			|| (legStation is not null
				&& string.Equals(searched.Id, legStation.Id, StringComparison.OrdinalIgnoreCase));

		return isStop
			? Position(legStation) ?? Position(searched) ?? fallback
			: Position(searched) ?? Position(legStation) ?? fallback;
	}

	public static MapScene FromJourney(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		var lines = new List<MapLine>();
		var markers = new List<MapMarker>();

		JourneyLeg? first = journey.Legs.Count > 0 ? journey.Legs[0] : null;
		JourneyLeg? last = journey.Legs.Count > 0 ? journey.Legs[^1] : null;

		Station? origin = journey.Origin ?? journey.From;
		Station? destination = journey.Destination ?? journey.To;

		(double Latitude, double Longitude)? startPoint =
			Terminal(origin, first?.From, first is not null ? FirstOf(LegPoints(first)) : null);

		(double Latitude, double Longitude)? endPoint =
			Terminal(destination, last?.To, last is not null ? LastOf(LegPoints(last)) : null);

		for (int index = 0; index < journey.Legs.Count; index++)
		{
			JourneyLeg leg = journey.Legs[index];
			var points = LegPoints(leg);

			if (points.Count >= 2)
			{
				lines.Add(
					new MapLine(
						points,
						ModeColor(leg.Mode),
						leg.Mode == TransitMode.Walk,
						leg.Mode == TransitMode.Walk ? 5 : 6));
			}

			string lineName =
				leg.Line?.Name is { Length: > 0 } name
					? name
					: Format.TransportMode(leg.Mode);

			// Every stop in between: a small quiet knot on the line (the name is in its popup).
			for (int stopIndex = 1; stopIndex < leg.Stops.Count - 1; stopIndex++)
			{
				StopTime between = leg.Stops[stopIndex];

				if (Position(between.Station) is { } knot)
				{
					markers.Add(
						new MapMarker(
							$"leg{index}-k{stopIndex}",
							knot.Latitude,
							knot.Longitude,
							string.Empty,
							MapMarkerKind.Knot,
							ModeColor(leg.Mode),
							between.Station.Name,
							$"{lineName} · {Format.TimeOrDash(between.RealtimeDeparture ?? between.ScheduledDeparture ?? between.RealtimeArrival ?? between.ScheduledArrival)}"));
				}
			}

			// Boarding and alighting stops; the start and end markers stand for the first and the last of them.
			if (index > 0
				&& (Position(leg.From) ?? FirstOf(points)) is { } board)
			{
				markers.Add(
					new MapMarker(
						$"leg{index}-from",
						board.Latitude,
						board.Longitude,
						string.Empty,
						MapMarkerKind.Stop,
						ModeColor(leg.Mode),
						leg.From.Name,
						$"{lineName} · {Format.TimeOrDash(leg.EffectiveDeparture)}"));
			}

			if (index < journey.Legs.Count - 1
				&& (Position(leg.To) ?? LastOf(points)) is { } alight)
			{
				markers.Add(
					new MapMarker(
						$"leg{index}-to",
						alight.Latitude,
						alight.Longitude,
						string.Empty,
						MapMarkerKind.Stop,
						ModeColor(leg.Mode),
						leg.To.Name,
						$"{lineName} · {Format.TimeOrDash(leg.EffectiveArrival)}"));
			}
		}

		foreach (JourneyTransfer transfer in journey.Transfers)
		{
			if (transfer.Path.Count >= 2)
			{
				lines.Add(new MapLine(transfer.Path, WalkColor, true, 4));
			}
		}

		if (startPoint is { } startAt)
		{
			markers.Add(
				new MapMarker(
					"start",
					startAt.Latitude,
					startAt.Longitude,
					string.Empty,
					MapMarkerKind.Start,
					null,
					origin?.Name ?? first?.From.Name,
					$"{Strings.MapStart} · {Format.TimeOrDash(journey.Departure)}"));
		}

		if (endPoint is { } endAt)
		{
			markers.Add(
				new MapMarker(
					"end",
					endAt.Latitude,
					endAt.Longitude,
					string.Empty,
					MapMarkerKind.End,
					null,
					destination?.Name ?? last?.To.Name,
					$"{Strings.MapEnd} · {Format.TimeOrDash(journey.Arrival)}"));
		}

		return new MapScene
		{
			Lines = lines,
			Markers = markers,
			Fit = true
		};
	}

	// ----- Run of a vehicle -----

	public static MapScene FromRun(
		IReadOnlyList<RunStop> stops,
		TransitMode mode,
		int vehicleIndex = -1,
		GeoPosition? vehicle = null,
		string? lineText = null)
	{
		ArgumentNullException.ThrowIfNull(stops);

		string color = ModeColor(mode);

		var located =
			stops
				.Select((stop, index) => (Stop: stop, Index: index, Position: Position(stop.Station)))
				.Where(item => item.Position is not null)
				.ToArray();

		var markers = new List<MapMarker>();
		var lines = new List<MapLine>();

		if (located.Length >= 2)
		{
			lines.Add(
				new MapLine(
					(List<(double Latitude, double Longitude)>)[.. located.Select(item => item.Position!.Value)],
					color,
					false,
					5));
		}

		for (int index = 0; index < located.Length; index++)
		{
			RunStop stop = located[index].Stop;
			(double latitude, double longitude) = located[index].Position!.Value;

			bool current = located[index].Index == vehicleIndex;
			bool passed = located[index].Index < vehicleIndex;

			markers.Add(
				new MapMarker(
					$"run{index}",
					latitude,
					longitude,
					string.Empty,
					current ? MapMarkerKind.Current : MapMarkerKind.Stop,
					passed ? "#9e9e9e" : color,
					stop.Station.Name,
					stop.Effective is { } time
						? $"{Format.Time(time)}{(Format.Delay(stop.Delay) is { } delay ? $" · {delay}" : string.Empty)}"
						: null));
		}

		// Where the provider says the vehicle is (TRIAS CurrentPosition), on top of the stops.
		if (vehicle is not null
			&& Position(vehicle.Latitude, vehicle.Longitude) is { } reported)
		{
			markers.Add(
				new MapMarker(
					"vehicle",
					reported.Latitude,
					reported.Longitude,
					lineText ?? string.Empty,
					MapMarkerKind.Vehicle,
					color,
					Strings.VehicleReported,
					null));
		}

		return new MapScene
		{
			Lines = lines,
			Markers = markers,
			Fit = true
		};
	}

	// ----- Live vehicles -----

	public static string DelayColor(TimeSpan? delay) =>
		delay is not { } value
			? Hex(Theme.ColorOf("InkMuted", Color.FromArgb("#607D8B")))
			: value.TotalMinutes >= 5
				? Hex(Theme.ColorOf("Cancelled", Color.FromArgb("#B91C1C")))
				: value.TotalMinutes >= 1
					? Hex(Theme.ColorOf("Delay", Color.FromArgb("#9A5B00")))
					: Hex(Theme.ColorOf("OnTime", Color.FromArgb("#15803D")));

	public static MapScene FromVehicles(
		IEnumerable<LiveVehicle> vehicles,
		bool fit)
	{
		ArgumentNullException.ThrowIfNull(vehicles);

		ExtrasStrings strings = Strings;

		return new MapScene
		{
			Fit = fit,
			Markers =
				(List<MapMarker>)
				[.. vehicles
					.Select(
						vehicle =>
						{
							string delay =
								vehicle.Delay is null
									? string.Empty
									: Format.Delay(vehicle.Delay) ?? strings.LiveOnTime;

							return new MapMarker(
								vehicle.Key,
								vehicle.Latitude,
								vehicle.Longitude,
								vehicle.Line.ToString(CultureInfo.InvariantCulture),
								MapMarkerKind.Vehicle,
								DelayColor(vehicle.Delay),
								string.Format(CultureInfo.CurrentCulture, strings.LiveLine, vehicle.Line),
								string.Join(
									" · ",
									new[]
									{
										string.Format(CultureInfo.CurrentCulture, strings.LiveRun, vehicle.Run),
										delay
									}.Where(part => part.Length > 0)));
						})]
		};
	}

	/// <summary>
	/// One followed run: its whole course drawn faintly, its stops, and the matched vehicle (when one was found)
	/// on top, in the colour of its delay.
	/// </summary>
	public static MapScene FromTrack(
		TrackTarget target,
		LiveVehicle? vehicle,
		bool fit) =>
		FromTracks((List<TrackTarget>)[target], (List<LiveVehicle?>)[vehicle], fit);

	/// <summary>The runs of a followed journey together: every course faintly, its stops, and each matched vehicle.</summary>
	public static MapScene FromTracks(
		IReadOnlyList<TrackTarget> targets,
		IReadOnlyList<LiveVehicle?> vehicles,
		bool fit)
	{
		ArgumentNullException.ThrowIfNull(targets);
		ArgumentNullException.ThrowIfNull(vehicles);

		var markers = new List<MapMarker>();
		var lines = new List<MapLine>();

		for (int t = 0; t < targets.Count; t++)
		{
			AddTrack(targets[t], t < vehicles.Count ? vehicles[t] : null, t, markers, lines);
		}

		return new MapScene
		{
			Lines = lines,
			Markers = markers,
			Fit = fit
		};
	}

	private static void AddTrack(
		TrackTarget target,
		LiveVehicle? vehicle,
		int number,
		List<MapMarker> markers,
		List<MapLine> lines)
	{
		string color = ModeColor(target.Mode);
		ExtrasStrings strings = Strings;

		var points =
			target.Course
				.Where(point => Position(point.Latitude, point.Longitude) is not null)
				.ToArray();

		if (points.Length >= 2)
		{
			lines.Add(
				new MapLine(
					(List<(double Latitude, double Longitude)>)[.. points.Select(point => (point.Latitude, point.Longitude))],
					color,
					false,
					5,
					0.4));
		}

		for (int index = 0; index < points.Length; index++)
		{
			CoursePoint point = points[index];

			markers.Add(
				new MapMarker(
					$"trk{number}_{index}",
					point.Latitude,
					point.Longitude,
					string.Empty,
					MapMarkerKind.Stop,
					color,
					point.Name,
					point.Time is { } time
						? Format.Time(time)
						: null));
		}

		if (vehicle is not null)
		{
			string delay =
				vehicle.Delay is null
					? string.Empty
					: Format.Delay(vehicle.Delay) ?? strings.LiveOnTime;

			markers.Add(
				new MapMarker(
					vehicle.Key,
					vehicle.Latitude,
					vehicle.Longitude,
					vehicle.Line.ToString(CultureInfo.InvariantCulture),
					MapMarkerKind.Vehicle,
					DelayColor(vehicle.Delay),
					string.Format(CultureInfo.CurrentCulture, strings.LiveLine, vehicle.Line),
					string.Join(
						" · ",
						new[]
						{
							string.Format(CultureInfo.CurrentCulture, strings.LiveRun, vehicle.Run),
							delay
						}.Where(part => part.Length > 0))));
		}
	}

	// ----- Stops and places -----

	public static MapScene FromStops(
		Location? stop,
		IEnumerable<Location> nearby,
		IEnumerable<ServicePoint> servicePoints)
	{
		ArgumentNullException.ThrowIfNull(nearby);
		ArgumentNullException.ThrowIfNull(servicePoints);

		var markers = new List<MapMarker>();

		if (stop is not null
			&& Position(stop.Latitude, stop.Longitude) is { } here)
		{
			markers.Add(
				new MapMarker(
					"stop",
					here.Latitude,
					here.Longitude,
					string.Empty,
					MapMarkerKind.Current,
					AccentColor,
					stop.Name,
					stop.Place));
		}

		int index = 0;

		foreach (Location other in nearby)
		{
			if (Position(other.Latitude, other.Longitude) is { } position)
			{
				markers.Add(
					new MapMarker(
						$"near{index++}",
						position.Latitude,
						position.Longitude,
						string.Empty,
						MapMarkerKind.Stop,
						AccentColor,
						other.Name,
						other.Place));
			}
		}

		index = 0;

		foreach (ServicePoint point in servicePoints)
		{
			if (Position(point.Latitude, point.Longitude) is { } position)
			{
				markers.Add(
					new MapMarker(
						$"service{index++}",
						position.Latitude,
						position.Longitude,
						string.Empty,
						MapMarkerKind.Poi,
						null,
						point.Name));
			}
		}

		return new MapScene
		{
			Markers = markers,
			Fit = true
		};
	}
}
