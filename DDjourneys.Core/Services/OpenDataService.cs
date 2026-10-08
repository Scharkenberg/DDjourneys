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
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		_provider is { } provider
			? provider.GetStopAccessibilityAsync(stop, timeout, cancellationToken)
			: Task.FromResult<IReadOnlyList<StopAccessibility>>([]);

	public Task<IReadOnlyList<ServicePoint>> GetServicePointsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 3000,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		_provider is { } provider
			? provider.GetServicePointsAsync(latitude, longitude, radiusMeters, timeout, cancellationToken)
			: Task.FromResult<IReadOnlyList<ServicePoint>>([]);
}
