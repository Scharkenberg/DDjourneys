using DDjourneys.Core.Models;

namespace DDjourneys.Core.Mapping;

/// <summary>The area a map currently shows, as the map page reports it after every pan or zoom.</summary>
public sealed record MapViewport(
	double South,
	double West,
	double North,
	double East,
	double Zoom,
	double Latitude,
	double Longitude)
{
	/// <summary>Distance from the centre to the farthest corner, capped (providers answer areas of a few km at most).</summary>
	public int RadiusMeters(int maximum = 2000) =>
		(int)Math.Clamp(
			Math.Max(
				GeoMath.DistanceMeters(Latitude, Longitude, North, East),
				GeoMath.DistanceMeters(Latitude, Longitude, South, West)),
			100,
			maximum);
}
