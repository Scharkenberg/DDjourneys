using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Services;

/// <summary>Open data of the city about the network (accessibility, service points).</summary>
public sealed class OpenDataService
{
	private readonly IOpenDataProvider? _provider;

	public OpenDataService(
		IEnumerable<IOpenDataProvider> providers)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_provider = providers.FirstOrDefault();
	}

	public bool IsAvailable =>
		_provider is not null;

	// City open data changes rarely; ten minutes keep a page that is opened again from asking twice.
	private readonly TtlCache<IReadOnlyList<StopAccessibility>> _accessibility = new(TimeSpan.FromMinutes(10));

	private readonly TtlCache<IReadOnlyList<ServicePoint>> _servicePoints = new(TimeSpan.FromMinutes(10), 32);

	public async Task<IReadOnlyList<StopAccessibility>> GetStopAccessibilityAsync(
		Location stop,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		if (_provider is not { } provider)
		{
			return [];
		}

		string? key = string.IsNullOrWhiteSpace(stop.Id) ? null : $"{stop.Id}|{stop.Name}";

		if (key is not null
			&& _accessibility.TryGet(key, out IReadOnlyList<StopAccessibility>? cached))
		{
			return cached;
		}

		IReadOnlyList<StopAccessibility> found =
			await provider.GetStopAccessibilityAsync(stop, timeout, cancellationToken).ConfigureAwait(false);

		if (key is not null
			&& found.Count > 0)
		{
			_accessibility.Set(key, found);
		}

		return found;
	}

	public async Task<IReadOnlyList<ServicePoint>> GetServicePointsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 3000,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		if (_provider is not { } provider)
		{
			return [];
		}

		// The position rounded to about 100 m: the same view of the map asks once.
		string key = string.Create(CultureInfo.InvariantCulture, $"{latitude:F3}|{longitude:F3}|{radiusMeters}");

		if (_servicePoints.TryGet(key, out IReadOnlyList<ServicePoint>? cached))
		{
			return cached;
		}

		IReadOnlyList<ServicePoint> found =
			await provider.GetServicePointsAsync(latitude, longitude, radiusMeters, timeout, cancellationToken).ConfigureAwait(false);

		if (found.Count > 0)
		{
			_servicePoints.Set(key, found);
		}

		return found;
	}
}
