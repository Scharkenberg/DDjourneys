using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>
/// Provides location search functionality.
/// </summary>
public interface ILocationProvider
{
	/// <summary>
	/// Searches for locations matching the supplied query.
	/// </summary>
	Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Searches for the given kinds of places. A provider that knows addresses and points of interest
	/// overrides this; the default filters what <see cref="SearchAsync(string, CancellationToken, TimeSpan?)"/> returns.
	/// </summary>
	async Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		PlaceKinds kinds,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<Location> found =
			await SearchAsync(
				query,
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		return kinds == PlaceKinds.All
			? found
			: [.. found.Where(place => kinds.HasFlag(place.Kind.ToFlag()))];
	}


	/// <summary>
	/// Searches for locations near the given coordinates.
	/// </summary>
	Task<IReadOnlyList<Location>> SearchByCoordinatesAsync(
		double latitude,
		double longitude,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// The address at a position (e.g. the device's), or null when the provider has none. Lets a journey
	/// start exactly where the passenger is; the router plans the walk to the stop.
	/// </summary>
	Task<Location?> ResolveAddressAsync(
		double latitude,
		double longitude,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		Task.FromResult<Location?>(null);
}
