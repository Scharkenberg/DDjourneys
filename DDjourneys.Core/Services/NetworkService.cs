using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>Route changes, stop lines, nearby stops and tariff zones, answered by the selected provider.</summary>
public sealed class NetworkService
{
	private readonly IEnumerable<INetworkInfoProvider> _providers;
	private readonly ProviderRegistry? _registry;

	public NetworkService(
		IEnumerable<INetworkInfoProvider> providers,
		ProviderRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_providers = providers;
		_registry = registry;
	}

	private INetworkInfoProvider? Provider =>
		_registry is null
			? _providers.FirstOrDefault()
			: _providers.FirstOrDefault(_registry.IsSelected);

	public Task<DisruptionReport> GetDisruptionsAsync(
		bool shortTermOnly = false,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetDisruptionsAsync(shortTermOnly, cancellationToken)
			: Task.FromResult(DisruptionReport.Empty);

	public Task<IReadOnlyList<DisruptionLine>> GetDisruptedLinesAsync(
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetDisruptedLinesAsync(cancellationToken)
			: Task.FromResult<IReadOnlyList<DisruptionLine>>([]);

	public Task<IReadOnlyList<StopLine>> GetStopLinesAsync(
		Location stop,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetStopLinesAsync(stop, cancellationToken)
			: Task.FromResult<IReadOnlyList<StopLine>>([]);

	public Task<IReadOnlyList<NearbyStop>> GetNearbyStopsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 500,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetNearbyStopsAsync(latitude, longitude, radiusMeters, cancellationToken)
			: Task.FromResult<IReadOnlyList<NearbyStop>>([]);

	public Task<TariffZone?> FindTariffZoneAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.FindTariffZoneAsync(latitude, longitude, cancellationToken)
			: Task.FromResult<TariffZone?>(null);
}
