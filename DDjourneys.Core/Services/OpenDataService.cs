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

	public Task<IReadOnlyList<StopAccessibility>> GetStopAccessibilityAsync(
		Location stop,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		_provider is { } provider
			? provider.GetStopAccessibilityAsync(stop, cancellationToken, timeout)
			: Task.FromResult<IReadOnlyList<StopAccessibility>>([]);

	public Task<IReadOnlyList<ServicePoint>> GetServicePointsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 3000,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		_provider is { } provider
			? provider.GetServicePointsAsync(latitude, longitude, radiusMeters, cancellationToken, timeout)
			: Task.FromResult<IReadOnlyList<ServicePoint>>([]);
}
