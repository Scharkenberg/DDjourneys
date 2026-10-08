namespace DDjourneys.Core.Models;

/// <summary>
/// Builds the run to follow from a journey leg, whatever the provider gave: the stops with positions and times when
/// they have them (VVO), else the leg's geometry with times spread along it by distance (TRIAS: its calls carry
/// no coordinates, its leg projection does).
/// </summary>
public static class TrackTargets
{
	/// <summary>Points kept of a long geometry: enough to follow the track, few enough to score quickly.</summary>
	private const int MaxPathPoints = 80;

	/// <summary>The target for <paramref name="leg"/>, or null when it cannot be matched (no numeric line, no times).</summary>
	public static TrackTarget? FromLeg(JourneyLeg leg, string line)
	{
		ArgumentNullException.ThrowIfNull(leg);
		ArgumentNullException.ThrowIfNull(line);

		CoursePoint[] stops =
			[.. leg.Stops
				.Where(stop => Valid(stop.Station.Latitude, stop.Station.Longitude))
				.Select(
					stop => new CoursePoint(
						stop.Station.Latitude!.Value,
						stop.Station.Longitude!.Value,
						stop.EffectiveDeparture ?? stop.EffectiveArrival,
						stop.Station.Name))];

		TrackTarget Make(IReadOnlyList<CoursePoint> course) =>
			new()
			{
				Line = line,
				Mode = leg.Mode,
				Direction = leg.Line?.Destination,
				Course = course
			};

		TrackTarget fromStops = Make(stops);

		if (fromStops.IsUsable)
		{
			return fromStops;
		}

		TrackTarget fromPath = Make(AlongPath(leg));

		return fromPath.IsUsable
			? fromPath
			: null;
	}

	/// <summary>The leg's geometry with the times of boarding and alighting spread over it by distance.</summary>
	public static IReadOnlyList<CoursePoint> AlongPath(JourneyLeg leg)
	{
		ArgumentNullException.ThrowIfNull(leg);

		(double Latitude, double Longitude)[] path =
			[.. leg.Path.Where(point => Valid(point.Latitude, point.Longitude))];

		if (path.Length < 2
			|| leg.EffectiveDeparture is not { } departure
			|| leg.EffectiveArrival is not { } arrival
			|| arrival <= departure)
		{
			return [];
		}

		if (path.Length > MaxPathPoints)
		{
			double step = (path.Length - 1) / (double)(MaxPathPoints - 1);

			path =
				[.. Enumerable
					.Range(0, MaxPathPoints)
					.Select(index => path[(int)Math.Round(index * step)])];
		}

		var distances = new double[path.Length];

		for (int index = 1; index < path.Length; index++)
		{
			distances[index] =
				distances[index - 1]
				+ Meters(path[index - 1].Latitude, path[index - 1].Longitude, path[index].Latitude, path[index].Longitude);
		}

		double total = distances[^1];

		if (total <= 0)
		{
			return [];
		}

		var course = new List<CoursePoint>(path.Length);

		for (int index = 0; index < path.Length; index++)
		{
			course.Add(
				new CoursePoint(
					path[index].Latitude,
					path[index].Longitude,
					departure + ((arrival - departure) * (distances[index] / total)),
					index == 0
						? leg.From.Name
						: index == path.Length - 1
							? leg.To.Name
							: null));
		}

		return course;
	}

	private static bool Valid(double? latitude, double? longitude) =>
		latitude is { } lat
		&& longitude is { } lon
		&& !(lat == 0 && lon == 0);

	private static double Meters(double lat1, double lon1, double lat2, double lon2)
	{
		const double Earth = 6371000;

		double dLat = (lat2 - lat1) * Math.PI / 180;
		double dLon = (lon2 - lon1) * Math.PI / 180;

		double a =
			(Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
			+ (Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));

		return 2 * Earth * Math.Asin(Math.Min(1, Math.Sqrt(a)));
	}
}
