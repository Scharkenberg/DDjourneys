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

	private static string AccentColor => ThemeRef("Accent", "#0B6E8A");

	/// <summary>The quiet grey of stops that do not matter right now: the run map greys its passed
	/// stops the same way.</summary>
	private static string DullColor => "#9e9e9e";

	/// <summary>
	/// A theme colour by reference ("@Key|#fallback"): <see cref="Resolve"/> turns it into the current colour each time
	/// the scene is sent, so maps follow theme changes.
	/// </summary>
	private static string ThemeRef(string key, string fallback) =>
		$"@{key}|{fallback}";

	/// <summary>A scene colour as it is drawn now: theme references resolved, plain colours unchanged.</summary>
	public static string Resolve(string color)
	{
		if (color.Length < 2 || color[0] != '@')
		{
			return color;
		}

		int bar = color.IndexOf('|', StringComparison.Ordinal);
		string key = bar > 1 ? color[1..bar] : color[1..];
		string fallback = bar > 1 ? color[(bar + 1)..] : "#808080";

		return Hex(Theme.ColorOf(key, Color.FromArgb(fallback)));
	}

	private static string Hex(Color color) =>
		$"#{(int)Math.Round(color.Red * 255):X2}{(int)Math.Round(color.Green * 255):X2}{(int)Math.Round(color.Blue * 255):X2}";

	private static ExtrasStrings Strings =>
		LocalizationService.Current.CurrentStrings.Extras;

	/// <summary>
	/// One tariff zone as a map polygon: the provider's own colour (the accent when it names none) over a
	/// quiet outline that also survives pale zone colours on a pale basemap, and the zone number as its label.
	/// The ring is simplified once here, not on every send.
	/// </summary>
	public static MapPolygon ZonePolygon(TariffZoneShape shape, bool dark, int maxPoints = 256) =>
		new(
			PolylineSimplify.SimplifyRing(shape.Ring, maxPoints),
			shape.Color is { Length: > 1 } ? shape.Color : ThemeRef("@Accent", "#0b6e8a"),
			dark ? 0.14 : 0.10,
			ThemeRef("@Outline", "#9e9e9e"),
			shape.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
			shape.CenterLat,
			shape.CenterLon);

	/// <summary>
	/// Opens the map page with a scene; says so (returns false) when there is nothing to show. <paramref name="from"/>
	/// (the asking page or view model) lets the map open beside it in a wide window (see <see cref="Panes"/>).
	/// </summary>
	public static async Task<bool> OpenAsync(
		MapScene scene,
		string title,
		object? from = null)
	{
		ArgumentNullException.ThrowIfNull(scene);

		if (scene.IsEmpty)
		{
			return false;
		}

		await Panes.GoToAsync(
			Routes.Map,
			new ShellNavigationQueryParameters
			{
				[Routes.MapScene] = scene,
				[Routes.MapTitle] = title
			},
			from);

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

	private static (double Latitude, double Longitude) ToPosition(RunStop stop) =>
			(stop.Station.Latitude ?? 0, stop.Station.Longitude ?? 0);

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

	/// <summary>
	/// The part of a route line that lies between the first and the last of the stops shown: the nearest
	/// vertices carry the split, the stretch in between is the route. Null when the line is unusable.
	/// </summary>
	private static IReadOnlyList<(double Latitude, double Longitude)>? Stretch(
		IReadOnlyList<(double Latitude, double Longitude)>? path,
		(double Latitude, double Longitude) first,
		(double Latitude, double Longitude) last)
	{
		if (path is not { Count: >= 2 })
		{
			return null;
		}

		int start = Nearest(path, first);
		int end = Nearest(path, last);

		if (start > end)
		{
			(start, end) = (end, start);
		}

		return end - start + 1 >= 2
			? [.. path.Skip(start).Take(end - start + 1)]
			: null;
	}

	/// <summary>The vertex of the line that is closest to a stop: where a split lands.</summary>
	private static int Nearest(
		IReadOnlyList<(double Latitude, double Longitude)> path,
		(double Latitude, double Longitude) at)
	{
		int best = 0;
		double bestDistance = double.MaxValue;

		for (int index = 0; index < path.Count; index++)
		{
			double distance =
				SquareMeters(at, path[index]);

			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = index;
			}
		}

		return best;
	}

	/// <summary>Squared distance of two points, close enough over a city: longitudes are closer together
	/// than latitudes.</summary>
	private static double SquareMeters(
		(double Latitude, double Longitude) one,
		(double Latitude, double Longitude) other)
	{
		const double LongitudeScale = 0.63;

		double dy = other.Latitude - one.Latitude;
		double dx = (other.Longitude - one.Longitude) * LongitudeScale;

		return (dy * 110_540) * (dy * 110_540) + (dx * 70_000) * (dx * 70_000);
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

		// The walk of every change: the footpath the transfer carries — the platform change at a stop as much
		// as the walk to another stop — and the walks before the first and after the last leg, which are
		// transfers without surrounding legs. Only a change between two different stops that no footpath
		// reaches draws a straight line between the stops instead (the plan sent to the tracking service
		// builds its walking episode the same way); a change at the same stop draws nothing.
		bool[] covered = new bool[Math.Max(0, journey.Legs.Count - 1)];

		foreach (JourneyTransfer transfer in journey.Transfers)
		{
			if (transfer.Path.Count < 2)
			{
				continue;
			}

			lines.Add(new MapLine(transfer.Path, WalkColor, true, 4));

			if (transfer.PreviousLegIndex is { } previous
				&& transfer.NextLegIndex is { } next)
			{
				for (int gap = Math.Max(0, previous); gap < next && gap < covered.Length; gap++)
				{
					covered[gap] = true;
				}
			}
		}

		for (int index = 0; index < journey.Legs.Count - 1; index++)
		{
			if (covered[index])
			{
				continue;
			}

			Station alight = journey.Legs[index].To;
			Station board = journey.Legs[index + 1].From;

			if (string.Equals(alight.Id, board.Id, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (Position(alight) is { } alightAt
				&& Position(board) is { } boardAt)
			{
				lines.Add(
					new MapLine(
						(List<(double Latitude, double Longitude)>)
						[(alightAt.Latitude, alightAt.Longitude), (boardAt.Latitude, boardAt.Longitude)],
						WalkColor,
						true,
						4));
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
		string? lineText = null,
		IReadOnlyList<(double Latitude, double Longitude)>? path = null)
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
			// The real line of the route when the provider has it, clipped to the stops shown; straight
			// between the stops otherwise.
			IReadOnlyList<(double Latitude, double Longitude)> course =
				Stretch(path, located[0].Position!.Value, located[^1].Position!.Value)
				?? [.. located.Select(item => item.Position!.Value)];

			lines.Add(
				new MapLine(
					course,
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
					passed ? DullColor : color,
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

	/// <summary>
	/// The course of a whole line on the map: the full geometry of the window (weight 5, the mode's colour -
	/// the path already covers the window, no stretching), its de-duplicated stops as small knots, the termini
	/// as labelled pins and the reported vehicle. A second course (the other direction) rides along dashed at
	/// half strength; it shares most stops with the first, so only the first carries knots.
	/// </summary>
	public static MapScene FromLineCourse(LineCourse course, bool fit = true) =>
			FromLineCourses([course], fit);

	public static MapScene FromLineCourses(IReadOnlyList<LineCourse> courses, bool fit = true)
	{
		ArgumentNullException.ThrowIfNull(courses);

		List<MapMarker> markers = [];
		List<MapLine> lines = [];

		for (int index = 0; index < courses.Count; index++)
		{
			LineCourse course = courses[index];
			string color = ModeColor(course.Mode);
			bool further = index > 0;

			List<RunStop> located =
				[.. course.Stops.Where(stop => stop.Station.Latitude is not null && stop.Station.Longitude is not null)];

			// The geometry: the window's path when there is one, else the stops as a polyline.
			IReadOnlyList<(double Latitude, double Longitude)> geometry =
				course.Path is { Count: > 1 }
						? course.Path
						: (List<(double Latitude, double Longitude)>)[.. located.Select(ToPosition)];

			if (geometry.Count > 1)
			{
				lines.Add(new MapLine(geometry, color, further, 5, further ? 0.4 : 1));
			}

			if (located.Count > 0)
			{
				RunStop from = located[0];
				RunStop to = located[^1];

				markers.Add(
					new MapMarker(
						$"line{index}-start",
						from.Station.Latitude!.Value,
						from.Station.Longitude!.Value,
						course.FirstTerminus ?? from.Station.Name,
						MapMarkerKind.Start,
						color));

				markers.Add(
					new MapMarker(
						$"line{index}-end",
						to.Station.Latitude!.Value,
						to.Station.Longitude!.Value,
						course.LastTerminus ?? to.Station.Name,
						MapMarkerKind.End,
						color));
			}

			// The stops in between: quiet knots whose popup names the stop and its time.
			if (!further)
			{
				for (int stopIndex = 1; stopIndex < located.Count - 1; stopIndex++)
				{
					RunStop stop = located[stopIndex];

					markers.Add(
						new MapMarker(
							$"k{stopIndex}",
							stop.Station.Latitude!.Value,
							stop.Station.Longitude!.Value,
							string.Empty,
							MapMarkerKind.Knot,
							DullColor,
							stop.Station.Name,
							Format.Time(stop.Effective)));
				}

				if (course.Vehicle is { } vehicle
						&& Position(vehicle.Latitude, vehicle.Longitude) is { } reported)
				{
					markers.Add(
						new MapMarker(
							"vehicle",
							reported.Latitude,
							reported.Longitude,
							course.LineName,
							MapMarkerKind.Vehicle,
							color,
							string.Format(CultureInfo.CurrentCulture, Strings.LiveLine, course.LineName),
							Strings.VehicleReported));
				}
			}
		}

		return new MapScene { Lines = lines, Markers = markers, Fit = fit };
	}

	public static string DelayColor(TimeSpan? delay) =>
		delay is not { } value
			? ThemeRef("InkMuted", "#607D8B")
			: value.TotalMinutes >= 5
				? ThemeRef("Cancelled", "#B91C1C")
				: value.TotalMinutes >= 1
					? ThemeRef("Delay", "#9A5B00")
					: ThemeRef("OnTime", "#15803D");

	/// <summary>
	/// The colour of a park &amp; ride site by how full it is: green while a fifth of it is free (and at least
	/// five spaces), amber while it is filling up, red when it is full, grey while nothing live is known. A site
	/// without numbers never gets a colour that sounds like a verdict.
	/// </summary>
	public static string ParkingColor(ParkingSite site)
	{
		if (!site.HasLive || site.Total <= 0)
		{
			return ThemeRef("InkMuted", "#607D8B");
		}

		return site.Free == 0
			? ThemeRef("Cancelled", "#B91C1C")
			: site.Free > Math.Max(5, site.Total * 0.2)
				? ThemeRef("OnTime", "#15803D")
				: ThemeRef("Delay", "#9A5B00");
	}

	/// <summary>
	/// The colour of a shared-bike station: green from three bikes, amber for one or two, grey when it is
	/// empty, not renting or unknown (a failed status feed shows stations, not wrong numbers).
	/// </summary>
	public static string BikeColor(SharedStation station)
	{
		if (station.IsRenting is false || station.Bikes <= 0)
		{
			return ThemeRef("InkMuted", "#607D8B");
		}

		return station.Bikes >= 3
			? ThemeRef("OnTime", "#15803D")
			: ThemeRef("Delay", "#9A5B00");
	}

	public static MapScene FromVehicles(
		IEnumerable<LiveVehicle> vehicles,
		bool fit,
		string? idPrefix = null)
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
								idPrefix is null ? vehicle.Key : $"{idPrefix}{vehicle.Key}",
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
	/// One followed run: its whole itinerary drawn faintly — the vehicle's chained journeys around the
	/// ride — the stops of the ride itself in the colour of its line, the rest quiet grey, the real line of
	/// the route when the provider has it, and the matched vehicle (when one was found) on top, in the colour
	/// of its delay.
	/// </summary>
	public static MapScene FromTrack(
		TrackTarget target,
		LiveVehicle? vehicle,
		bool fit) =>
		FromTracks((List<TrackTarget>)[target], (List<LiveVehicle?>)[vehicle], fit);

	/// <summary>The runs of a followed journey together: every itinerary faintly, its stops, and each matched vehicle.</summary>
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

		// The vehicle's whole itinerary when it is known (the sliding window of the run answer); the run
		// itself otherwise. Both are built positioned-only, so the ride's span indexes the list as is.
		IReadOnlyList<CoursePoint> source =
			target.Itinerary ?? target.Course;

		var points =
			source
				.Where(point => Position(point.Latitude, point.Longitude) is not null)
				.ToArray();

		// The ride within the itinerary: boarding to alighting. Without a span (no itinerary, or one that
		// does not contain the leg's stops) every stop is the ride's, as the map has always drawn it.
		bool NoSpan() =>
			target.RideStart < 0
			|| target.RideEnd < target.RideStart;

		bool Ride(int index) =>
			NoSpan()
			|| (index >= target.RideStart && index <= target.RideEnd);

		if (points.Length >= 2)
		{
			// The real line of the route when the provider has it, clipped to the itinerary; straight
			// between the stops otherwise.
			IReadOnlyList<(double Latitude, double Longitude)> course =
				Stretch(
					path: target.Path,
					first: (points[0].Latitude, points[0].Longitude),
					last: (points[^1].Latitude, points[^1].Longitude))
				?? [.. points.Select(point => (point.Latitude, point.Longitude))];

			lines.Add(
				new MapLine(
					course,
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
					Ride(index) ? color : DullColor,
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
