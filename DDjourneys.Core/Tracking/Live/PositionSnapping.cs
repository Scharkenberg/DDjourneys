namespace DDjourneys.Core.Tracking.Live;

/// <summary>
/// Snapping the device's position onto the stop path of the run being followed (roadmap #19): the point is
/// projected onto the nearest of the straight segments between consecutive stops. Pure geometry - no map, no
/// permission, no timing; the freshness decision lives here too so both platforms ask the same question.
/// </summary>
public static class PositionSnapping
{
	/// <summary>
	/// A live position older than this is no longer where the vehicle is - the rule of the map's vehicles
	/// layer (<c>LiveVehicleLayer</c>), mirrored here so the "you are probably here" dot uses the same clock.
	/// </summary>
	public static readonly TimeSpan MaxVehicleAge = TimeSpan.FromSeconds(90);

	/// <summary>
	/// The position snapped onto the path: the projection onto the nearest segment, clamped to that segment's
	/// ends (a point beyond the last stop lands on it, never past it). Null when the path has no segment.
	/// </summary>
	public static (double Latitude, double Longitude)? Snap(
		double latitude,
		double longitude,
		IReadOnlyList<(double Latitude, double Longitude)> path)
	{
		ArgumentNullException.ThrowIfNull(path);

		if (path.Count < 2)
		{
			return null;
		}

		(double Latitude, double Longitude)? best = null;
		double bestDistance = double.MaxValue;

		for (int i = 1; i < path.Count; i++)
		{
			(double aLat, double aLon) = path[i - 1];
			(double bLat, double bLon) = path[i];

			double dx = bLat - aLat;
			double dy = bLon - aLon;
			double squared = dx * dx + dy * dy;

			double t =
				squared <= double.Epsilon
					? 0
					: Math.Clamp(
						((latitude - aLat) * dx + (longitude - aLon) * dy) / squared,
						0,
						1);

			double pLat = aLat + t * dx;
			double pLon = aLon + t * dy;

			double distance =
				Math.Pow(latitude - pLat, 2) + Math.Pow(longitude - pLon, 2);

			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = (pLat, pLon);
			}
		}

		return best;
	}

	/// <summary>
	/// Whether a live position still says where the vehicle is: not older than <see cref="MaxVehicleAge"/>.
	/// A missing position (null) is never fresh - that is the whole point of the snapped dot.
	/// </summary>
	public static bool IsFresh(DateTimeOffset? vehicleAt, DateTimeOffset now) =>
		vehicleAt is { } at && now - at <= MaxVehicleAge;
}
