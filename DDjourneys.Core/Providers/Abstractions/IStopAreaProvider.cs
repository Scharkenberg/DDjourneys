using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>Stops in an area, for a map: every stop that lies around a position, up to a limit.</summary>
public interface IStopAreaProvider
{
	/// <summary>Stops within <paramref name="radiusMeters"/> of a position, nearest first, at most <paramref name="limit"/>.</summary>
	Task<IReadOnlyList<NearbyStop>> GetStopsAroundAsync(
		double latitude,
		double longitude,
		int radiusMeters,
		int limit,
		CancellationToken cancellationToken = default);
}
