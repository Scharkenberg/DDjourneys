using DDjourneys.Core.Mapping;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>
/// The pure part of the map's vehicle layer: which of the vehicles belong in a viewport - inside its
/// bounds plus a margin, not older than the age limit, and when there are too many, the ones nearest
/// the centre. Testable without the stream or the map.
/// </summary>
public static class VehicleLayering
{
	/// <summary>
	/// The vehicles worth publishing for a viewport. The margin is a fifth of the viewport's span in each
	/// direction, so vehicles just outside the frame slide in instead of popping; the cap keeps the payload
	/// small by keeping the vehicles nearest the centre.
	/// </summary>
	public static IReadOnlyList<LiveVehicle> Within(
		MapViewport viewport,
		IEnumerable<LiveVehicle> vehicles,
		int cap = 250,
		TimeSpan maxAge = default,
		DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(viewport);
		ArgumentNullException.ThrowIfNull(vehicles);

		DateTimeOffset at = now ?? DateTimeOffset.UtcNow;
		TimeSpan age = maxAge == default ? TimeSpan.FromSeconds(90) : maxAge;

		double latMargin = (viewport.North - viewport.South) * 0.1;
		double lonMargin = (viewport.East - viewport.West) * 0.1;

		List<LiveVehicle> kept = [];

		foreach (LiveVehicle vehicle in vehicles)
		{
			if (at - vehicle.Time > age)
			{
				continue;
			}

			if (vehicle.Latitude > viewport.North + latMargin
					|| vehicle.Latitude < viewport.South - latMargin
					|| vehicle.Longitude > viewport.East + lonMargin
					|| vehicle.Longitude < viewport.West - lonMargin)
			{
				continue;
			}

			kept.Add(vehicle);
		}

		if (kept.Count <= cap)
		{
			return kept;
		}

		// Too many: the ones nearest the centre of the viewport are the ones the user is looking at.
		List<LiveVehicle> nearest =
			[.. kept.OrderBy(
				vehicle => GeoMath.DistanceMeters(viewport.Latitude, viewport.Longitude, vehicle.Latitude, vehicle.Longitude))];

		nearest.RemoveRange(cap, nearest.Count - cap);

		return nearest;
	}
}
