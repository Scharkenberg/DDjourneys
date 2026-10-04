namespace DDjourneys.Core.Models;

/// <summary>
/// Decides which live vehicle is the run of a <see cref="TrackTarget"/>. A vehicle fits when it is on the course
/// (close to the line through the stops), when it is there at the time the course says (the timetable with its
/// real-time delays, evaluated at the time of the vehicle's last position) and, when its previous position is
/// known, when it moves in the direction of the course. Vehicles of the same line in the other direction pass the
/// same track but fail the time test; the run before or after on the same direction fails it by a headway.
/// </summary>
public static class RunMatcher
{
	/// <summary>How far from the line of stops a position may be (stops are joined by straight lines).</summary>
	public const double MaxOffCourseMeters = 220;

	/// <summary>How far the vehicle's time may be from the course's time at the same place.</summary>
	public static readonly TimeSpan MaxTimeDifference = TimeSpan.FromMinutes(4);

	/// <summary>Smaller is better; null when the vehicle cannot be this run.</summary>
	public static double? Score(
		TrackTarget target,
		LiveVehicle vehicle,
		LiveVehicle? previous = null)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(vehicle);

		if (target.LineNumber is { } line
			&& vehicle.Line != line)
		{
			return null;
		}

		CoursePoint[] course = [.. target.Course];

		if (course.Length < 2)
		{
			return null;
		}

		double bestDistance = double.MaxValue;
		double bestSeconds = double.MaxValue;
		double bestBearing = double.NaN;

		for (int i = 0; i + 1 < course.Length; i++)
		{
			CoursePoint a = course[i];
			CoursePoint b = course[i + 1];

			(double distance, double fraction) =
				Project(vehicle.Latitude, vehicle.Longitude, a, b);

			if (distance > MaxOffCourseMeters
				|| a.Time is not { } ta
				|| b.Time is not { } tb)
			{
				continue;
			}

			DateTimeOffset expected = ta + (tb - ta) * fraction;

			double seconds = Math.Abs((vehicle.Time - expected).TotalSeconds);

			// The segment that explains both position and time best wins (a loop passes a place twice).
			if (seconds + distance < bestSeconds + bestDistance)
			{
				bestDistance = distance;
				bestSeconds = seconds;
				bestBearing = Bearing(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
			}
		}

		if (bestDistance == double.MaxValue
			|| bestSeconds > MaxTimeDifference.TotalSeconds)
		{
			return null;
		}

		if (previous is not null
			&& !double.IsNaN(bestBearing)
			&& Meters(previous.Latitude, previous.Longitude, vehicle.Latitude, vehicle.Longitude) > 15)
		{
			double heading =
				Bearing(previous.Latitude, previous.Longitude, vehicle.Latitude, vehicle.Longitude);

			double turn = Math.Abs(((heading - bestBearing + 540) % 360) - 180);

			// A tram on a curve may be 60° off the chord of the segment; the other direction is about 180°.
			if (turn > 110)
			{
				return null;
			}
		}

		return bestSeconds + bestDistance * 0.5;
	}

	// ----- geometry (equirectangular approximation: exact enough over a few hundred metres) -----

	private static (double X, double Y) Local(double latitude, double longitude, double originLatitude) =>
		(
			(longitude) * 111_320 * Math.Cos(originLatitude * Math.PI / 180),
			latitude * 110_540);

	private static (double Distance, double Fraction) Project(
		double latitude,
		double longitude,
		CoursePoint a,
		CoursePoint b)
	{
		(double px, double py) = Local(latitude, longitude, a.Latitude);
		(double ax, double ay) = Local(a.Latitude, a.Longitude, a.Latitude);
		(double bx, double by) = Local(b.Latitude, b.Longitude, a.Latitude);

		double dx = bx - ax;
		double dy = by - ay;
		double lengthSquared = dx * dx + dy * dy;

		double fraction =
			lengthSquared <= 0
				? 0
				: Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);

		double cx = ax + dx * fraction;
		double cy = ay + dy * fraction;

		return (Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)), fraction);
	}

	private static double Meters(double lat1, double lon1, double lat2, double lon2)
	{
		(double x1, double y1) = Local(lat1, lon1, lat1);
		(double x2, double y2) = Local(lat2, lon2, lat1);

		return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
	}

	/// <summary>Compass bearing in degrees (0 = north) of the move from the first point to the second.</summary>
	private static double Bearing(double lat1, double lon1, double lat2, double lon2)
	{
		(double x1, double y1) = Local(lat1, lon1, lat1);
		(double x2, double y2) = Local(lat2, lon2, lat1);

		return (Math.Atan2(x2 - x1, y2 - y1) * 180 / Math.PI + 360) % 360;
	}
}
