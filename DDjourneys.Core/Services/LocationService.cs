using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Services;

/// <summary>
/// Implementation of ILocationService that provides location lookup and GPS functionality.
/// </summary>
public sealed class LocationService : ILocationService
{
	private readonly IEnumerable<ILocationProvider> _providers;
	private readonly ProviderRegistry? _registry;
	private readonly IGeolocation? _geolocation;


	/// <param name="providers">All registered location providers.</param>
	/// <param name="registry">When given, the provider the user selected answers.</param>
	/// <param name="geolocation">Optional GPS service for getting current device location.</param>
	public LocationService(
		IEnumerable<ILocationProvider> providers,
		ProviderRegistry? registry = null,
		IGeolocation? geolocation = null)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_providers = providers;
		_registry = registry;
		_geolocation = geolocation;
	}


	/// <summary>
	/// Searches for locations matching user input.
	/// </summary>
	public Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ILocationProvider? provider =
			_registry is null
				? _providers.FirstOrDefault()
				: _providers.FirstOrDefault(_registry.IsSelected);

		return provider is null
			? Task.FromResult<IReadOnlyList<Location>>([])
			: provider.SearchAsync(
					query,
					cancellationToken,
					timeout);
	}


	/// <summary>
	/// Searches for locations near the given coordinates.
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


	/// <summary>
	/// Gets the current device location using GPS.
	/// </summary>
	public async Task<Location?> GetCurrentLocationAsync()
	{
		if (_geolocation is null)
		{
			return null;
		}

		try
		{
			Microsoft.Maui.Devices.Sensors.Location? location = await _geolocation.GetLastKnownLocationAsync();

			if (location is not null)
			{
				return new Location
				{
					Name = LocalizationService.Current.CurrentStrings.Plan.CurrentLocation,
					Latitude = location.Latitude,
					Longitude = location.Longitude
				};
			}

			// If no cached location, try to get fresh one
			location = await _geolocation.GetLocationAsync(
				new Microsoft.Maui.Devices.Sensors.LocationRequest
				{
					DesiredAccuracy = Microsoft.Maui.Devices.Sensors.LocationAccuracy.Medium,
					Timeout = TimeSpan.FromSeconds(15)
				});

			if (location is not null)
			{
				return new Location
				{
					Name = LocalizationService.Current.CurrentStrings.Plan.CurrentLocation,
					Latitude = location.Latitude,
					Longitude = location.Longitude
				};
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"GetCurrentLocationAsync failed: {ex.Message}");
		}

		return null;
	}
}
