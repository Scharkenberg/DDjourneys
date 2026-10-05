using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>
/// The stops in the area a map shows, from the selected provider. A provider that cannot list an area is asked
/// for the stops around the centre instead (what the location search gives), so every provider has some answer.
/// </summary>
public sealed class StopAreaService(
	IEnumerable<IStopAreaProvider> providers,
	LocationService locations,
	ProviderRegistry registry)
{
	/// <summary>Most stops a map shows at once; more is clutter, and more requests.</summary>
	public const int MaxStops = 80;

	public async Task<IReadOnlyList<NearbyStop>> GetStopsAsync(
		double latitude,
		double longitude,
		int radiusMeters,
		CancellationToken cancellationToken = default)
	{
		IStopAreaProvider? provider =
			providers.FirstOrDefault(registry.IsSelected);

		if (provider is not null)
		{
			IReadOnlyList<NearbyStop> found =
				await provider
					.GetStopsAroundAsync(latitude, longitude, radiusMeters, MaxStops, cancellationToken)
					.ConfigureAwait(false);

			if (found.Count > 0)
			{
				return found;
			}
		}

		IReadOnlyList<Location> stops =
			await locations
				.SearchByCoordinatesAsync(latitude, longitude, TimeSpan.FromSeconds(8), cancellationToken)
				.ConfigureAwait(false);

		return
			[.. stops
				.Where(stop => stop.Latitude is not null && stop.Longitude is not null)
				.Select(
					stop => new NearbyStop
					{
						Stop = stop,
						DistanceMeters =
							(int)Math.Round(
								GeoMath.DistanceMeters(latitude, longitude, stop.Latitude!.Value, stop.Longitude!.Value))
					})
				.Where(stop => stop.DistanceMeters <= radiusMeters)
				.OrderBy(stop => stop.DistanceMeters)];
	}
}
