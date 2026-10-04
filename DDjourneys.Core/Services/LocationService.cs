using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Services;

/// <summary>
/// Provides location lookup functionality.
/// </summary>
public sealed class LocationService
{
	private readonly IEnumerable<ILocationProvider> _providers;
	private readonly ProviderRegistry? _registry;


	/// <param name="providers">All registered location providers.</param>
	/// <param name="registry">When given, the provider the user selected answers.</param>
	public LocationService(
		IEnumerable<ILocationProvider> providers,
		ProviderRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_providers = providers;
		_registry = registry;
	}


	/// <summary>
	/// Searches for locations matching user input.
	/// </summary>
	public Task<IReadOnlyList<Location>> SearchAsync(
	string query,
	CancellationToken cancellationToken = default,
	TimeSpan? timeout = null,
	PlaceKinds kinds = PlaceKinds.Stops)
	{
		ILocationProvider? provider =
			_registry is null
				? _providers.FirstOrDefault()
				: _providers.FirstOrDefault(_registry.IsSelected);

		return provider is null
			? Task.FromResult<IReadOnlyList<Location>>([])
			: provider.SearchAsync(
				query,
				kinds,
				cancellationToken,
				timeout);
	}


	/// <summary>
	/// Finds the stops around a position (e.g. the device's), nearest first. Routing needs a stop, so a
	/// position is turned into one this way.
	/// </summary>
	public Task<IReadOnlyList<Location>> SearchByCoordinatesAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ILocationProvider? provider =
			_registry is null
				? _providers.FirstOrDefault()
				: _providers.FirstOrDefault(_registry.IsSelected);

		return provider is null
			? Task.FromResult<IReadOnlyList<Location>>([])
			: provider.SearchByCoordinatesAsync(
				latitude,
				longitude,
				cancellationToken,
				timeout);
	}


	/// <summary>The address at a position, or null (see <see cref="ILocationProvider.ResolveAddressAsync"/>).</summary>
	public Task<Location?> ResolveAddressAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ILocationProvider? provider =
			_registry is null
				? _providers.FirstOrDefault()
				: _providers.FirstOrDefault(_registry.IsSelected);

		return provider is null
			? Task.FromResult<Location?>(null)
			: provider.ResolveAddressAsync(
				latitude,
				longitude,
				cancellationToken,
				timeout);
	}
}
