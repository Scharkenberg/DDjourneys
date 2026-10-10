namespace DDjourneys.Core.Mapping;

/// <summary>
/// Douglas-Peucker in metres, on the equirectangular approximation: at the latitude of the VVO area
/// (about 51 degrees) a degree of longitude is the cosine of it, about 0.63, of a degree of latitude in
/// width. Good enough for simplifying outlines; the exact distance is never promised.
/// </summary>
public static class PolylineSimplify
{
	private const double LongitudeScale = 0.63;

	/// <summary>
	/// Douglas-Peucker: every point further than <paramref name="toleranceMeters"/> from the line through its
	/// neighbours survives; the first and the last point always do. An open polyline (a route) stays open.
	/// </summary>
	public static IReadOnlyList<(double Latitude, double Longitude)> Simplify(
		IReadOnlyList<(double Latitude, double Longitude)> points,
		double toleranceMeters)
	{
		if (points.Count <= 2
			|| toleranceMeters <= 0)
		{
			return points;
		}

		var keep = new bool[points.Count];

		keep[0] = true;
		keep[^1] = true;

		Mark(points, 0, points.Count - 1, toleranceMeters * toleranceMeters, keep);

		return
			(List<(double Latitude, double Longitude)>)[.. points.Where((_, i) => keep[i])];
	}

	/// <summary>
	/// Simplified to at most <paramref name="maxPoints"/>: the tolerance doubles until the result fits, and stops
	/// at 400 m (that already flattens everything city-scale; a single Douglas-Peucker pass cannot promise a cap,
	/// which is why this iterates).
	/// </summary>
	public static IReadOnlyList<(double Latitude, double Longitude)> SimplifyCapped(
		IReadOnlyList<(double Latitude, double Longitude)> points,
		int maxPoints = 256,
		double startToleranceMeters = 25)
	{
		double tolerance = Math.Max(1, startToleranceMeters);

		IReadOnlyList<(double Latitude, double Longitude)> result = points;

		while (true)
		{
			result = Simplify(points, tolerance);

			if (result.Count <= maxPoints
				|| tolerance >= 400)
			{
				return result;
			}

			tolerance *= 2;
		}
	}

	/// <summary>
	/// A ring (closed outline) simplified without its closing point: the duplicate anchor breaks the recursion
	/// (the segment endpoints coincide), so it is stripped first and re-appended after. A ring that arrives open
	/// is closed for the caller that needs a closed one (the map page).
	/// </summary>
	public static IReadOnlyList<(double Latitude, double Longitude)> SimplifyRing(
		IReadOnlyList<(double Latitude, double Longitude)> ring,
		int maxPoints = 256,
		double startToleranceMeters = 25)
	{
		if (ring.Count <= 3)
		{
			return ring;
		}

		bool closed =
			ring[0].Latitude == ring[^1].Latitude
			&& ring[0].Longitude == ring[^1].Longitude;

		var open =
			(List<(double Latitude, double Longitude)>)
			[.. (closed ? ring.Take(ring.Count - 1) : ring)];

		IReadOnlyList<(double Latitude, double Longitude)> simplified =
			SimplifyCapped(open, maxPoints, startToleranceMeters);

		return simplified.Count < 3
			? simplified
			: (List<(double Latitude, double Longitude)>)[.. simplified, simplified[0]];
	}

	private static void Mark(
		IReadOnlyList<(double Latitude, double Longitude)> points,
		int first,
		int last,
		double toleranceSquared,
		bool[] keep)
	{
		if (last - first < 2)
		{
			return;
		}

		(double latA, double lonA) = points[first];
		(double latB, double lonB) = points[last];

		// Metres per degree: a degree of longitude is the cosine of the latitude wide - at the latitude of
		// the VVO area (about 51 degrees) that is LongitudeScale, which is why it is a constant here.
		double metresLat = 111320;
		double metresLon = 111320 * LongitudeScale;

		double ax = lonA * metresLon;
		double ay = latA * metresLat;
		double bx = lonB * metresLon;
		double by = latB * metresLat;

		double dx = bx - ax;
		double dy = by - ay;
		double lengthSquared = (dx * dx) + (dy * dy);

		int furthest = -1;
		double furthestSquared = toleranceSquared;

		for (int i = first + 1; i < last; i++)
		{
			(double lat, double lon) = points[i];

			double px = (lon * metresLon) - ax;
			double py = (lat * metresLat) - ay;

			// Distance of the point from the line through the anchors; a degenerate segment (the anchors
			// coincide) falls back to the plain distance from the first anchor.
			double distanceSquared =
				lengthSquared > 0
					? ((px * dy) - (py * dx)) * ((px * dy) - (py * dx)) / lengthSquared
					: (px * px) + (py * py);

			if (distanceSquared > furthestSquared)
			{
				furthestSquared = distanceSquared;
				furthest = i;
			}
		}

		if (furthest < 0)
		{
			return;
		}

		keep[furthest] = true;

		Mark(points, first, furthest, toleranceSquared, keep);
		Mark(points, furthest, last, toleranceSquared, keep);
	}
}
