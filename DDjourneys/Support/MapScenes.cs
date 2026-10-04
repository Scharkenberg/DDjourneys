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

		var stops =
			leg.Stops
				.Select(stop => Position(stop.Station))
				.OfType<(double Latitude, double Longitude)>()
				.ToArray();

		if (stops.Length >= 2)
		{
			return stops;
		}

		return
			[.. new[] { Position(leg.From), Position(leg.To) }
				.OfType<(double Latitude, double Longitude)>()];
	}

	// ----- Journey -----

	public static MapScene FromJourney(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		var lines = new List<MapLine>();
		var markers = new List<MapMarker>();

		for (int index = 0; index < journey.Legs.Count; index++)
		{
			JourneyLeg leg = journey.Legs[index];
			var points = LegPoints(leg);

			if (points.Count >= 2)
			{
				lines.Add(new MapLine(points, ModeColor(leg.Mode), false, 6));
			}

			string lineName =
				leg.Line?.Name is { Length: > 0 } name
					? name
					: Format.TransportMode(leg.Mode);

			if ((Position(leg.From) ?? FirstOf(points)) is { } start)
			{
				markers.Add(
					new MapMarker(
						$"leg{index}-from",
						start.Latitude,
						start.Longitude,
						string.Empty,
						MapMarkerKind.Stop,
						ModeColor(leg.Mode),
						leg.From.Name,
						$"{lineName} · {Format.TimeOrDash(leg.EffectiveDeparture)}"));
			}

			if (index == journey.Legs.Count - 1
				&& (Position(leg.To) ?? LastOf(points)) is { } end)
			{
				markers.Add(
					new MapMarker(
						$"leg{index}-to",
						end.Latitude,
						end.Longitude,
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

		Station? origin = journey.Origin ?? journey.From;
		Station? destination = journey.Destination ?? journey.To;

		JourneyLeg? first = journey.Legs.FirstOrDefault();
		JourneyLeg? last = journey.Legs.LastOrDefault();

		if ((Position(origin)
				?? (first is not null ? FirstOf(LegPoints(first)) : null)) is { } startPoint)
		{
			markers.Add(
				new MapMarker(
					"start",
					startPoint.Latitude,
					startPoint.Longitude,
					string.Empty,
					MapMarkerKind.Start,
					null,
					origin?.Name,
					Strings.MapStart));
		}

		if ((Position(destination)
				?? (last is not null ? LastOf(LegPoints(last)) : null)) is { } endPoint)
		{
			markers.Add(
				new MapMarker(
					"end",
					endPoint.Latitude,
					endPoint.Longitude,
					string.Empty,
					MapMarkerKind.End,
					null,
					destination?.Name,
					Strings.MapEnd));
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
		int vehicleIndex = -1)
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
					[.. located.Select(item => item.Position!.Value)],
					color,
					false,
					5));
		}

		for (int index = 0; index < located.Length; index++)
		{
			RunStop stop = located[index].Stop;
			(double Latitude, double Longitude) position = located[index].Position!.Value;

			bool current = located[index].Index == vehicleIndex;
			bool passed = located[index].Index < vehicleIndex;

			markers.Add(
				new MapMarker(
					$"run{index}",
					position.Latitude,
					position.Longitude,
					string.Empty,
					current ? MapMarkerKind.Current : MapMarkerKind.Stop,
					passed ? "#9e9e9e" : color,
					stop.Station.Name,
					stop.Effective is { } time
						? $"{Format.Time(time)}{(Format.Delay(stop.Delay) is { } delay ? $" · {delay}" : string.Empty)}"
						: null));
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
