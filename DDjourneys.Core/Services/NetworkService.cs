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

	// The lines of a stop change with the timetable, not while somebody reads: ten minutes are plenty.
	private readonly TtlCache<IReadOnlyList<StopLine>> _stopLines = new(TimeSpan.FromMinutes(10));

	public async Task<IReadOnlyList<StopLine>> GetStopLinesAsync(
		Location stop,
		CancellationToken cancellationToken = default)
	{
		if (Provider is not { } provider)
		{
			return [];
		}

		// Per provider (its ids mean something else elsewhere); a stop without an id is not cached.
		string? key = string.IsNullOrWhiteSpace(stop.Id) ? null : $"{provider.GetType().FullName}|{stop.Id}";

		if (key is not null
			&& _stopLines.TryGet(key, out IReadOnlyList<StopLine>? cached))
		{
			return cached;
		}

		IReadOnlyList<StopLine> lines = await provider.GetStopLinesAsync(stop, cancellationToken).ConfigureAwait(false);

		if (key is not null
			&& lines.Count > 0)
		{
			_stopLines.Set(key, lines);
		}

		return lines;
	}

	public Task<IReadOnlyList<NearbyStop>> GetNearbyStopsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 500,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetNearbyStopsAsync(latitude, longitude, radiusMeters, cancellationToken)
			: Task.FromResult<IReadOnlyList<NearbyStop>>([]);

	public Task<IReadOnlyList<TariffZoneShape>> GetTariffZonesAsync(
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetTariffZonesAsync(cancellationToken)
			: Task.FromResult<IReadOnlyList<TariffZoneShape>>([]);

	public Task<TariffZone?> FindTariffZoneAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.FindTariffZoneAsync(latitude, longitude, cancellationToken)
			: Task.FromResult<TariffZone?>(null);
}
