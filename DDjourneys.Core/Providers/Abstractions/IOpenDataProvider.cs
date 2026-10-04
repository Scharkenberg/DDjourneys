using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>City open data about the network: stop accessibility and service points.</summary>
public interface IOpenDataProvider
{
	/// <summary>The platforms at (or near) a stop with what the city publishes about them.</summary>
	Task<IReadOnlyList<StopAccessibility>> GetStopAccessibilityAsync(
		Location stop,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null);

	/// <summary>Service points within <paramref name="radiusMeters"/> of a position, nearest first.</summary>
	Task<IReadOnlyList<ServicePoint>> GetServicePointsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 3000,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null);
}
