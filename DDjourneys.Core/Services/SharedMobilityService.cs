using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Shared;

namespace DDjourneys.Core.Services;

/// <summary>
/// Shared bikes for the map layer. The feeds are cached per feed, not per operator: the skeleton
/// (station_information, near-static) for an hour, the counts (station_status) at most a minute -
/// the feed's own ttl when it asks for less. A failed answer is never kept, so the next look asks
/// again; a dead status feed does not evict the hour-old skeleton.
/// </summary>
public sealed class SharedMobilityService
{
	private const int StatusSeconds = 60;

	private sealed record Operator(string Id, string Name, Uri Discovery, Uri Website);

	// v1 ships MOBIbike/Nextbike only; Lime Dresden is dockless (its useful feed is free_bike_status,
	// reserved for v1.1 together with off-station scooters).
	private static readonly Operator[] Operators =
	[
		new("nextbike", "MOBIbike", new Uri(InterfaceSchemas.GbfsUrl), new Uri("https://www.nextbike.de/"))
	];

	private readonly GbfsClient _client;

	private readonly TtlCache<GbfsDiscovery> _discovery = new(TimeSpan.FromHours(1), 8);
	private readonly TtlCache<GbfsStationInformation> _information = new(TimeSpan.FromHours(1), 8);

	// The status cache needs a lifetime the feed itself can shorten, which TtlCache cannot express.
	private readonly Dictionary<string, (DateTimeOffset At, GbfsStationStatus Value)> _status = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TimeSpan> _statusLifetime = new(StringComparer.Ordinal);

	private readonly SemaphoreSlim _gate = new(1, 1);

	public SharedMobilityService(GbfsClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		_client = client;
	}

	public async Task<IReadOnlyList<SharedStation>> GetStationsAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			List<SharedStation> all = [];

			foreach (Operator stationOperator in Operators)
			{
				all.AddRange(await GetOperatorAsync(stationOperator, timeout, cancellationToken).ConfigureAwait(false));
			}

			return all;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<IReadOnlyList<SharedStation>> GetOperatorAsync(
		Operator stationOperator,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
	{
		string discoveryKey = $"{stationOperator.Id}|discovery";

		GbfsDiscovery? discovery =
			_discovery.TryGet(discoveryKey, out GbfsDiscovery? cachedDiscovery)
				? cachedDiscovery
				: await _client.GetDiscoveryAsync(stationOperator.Discovery, timeout, cancellationToken).ConfigureAwait(false);

		if (discovery is null)
		{
			return [];
		}

		_discovery.Set(discoveryKey, discovery);

		Uri? informationUrl = GbfsClient.FeedUrl(discovery, "station_information");

		if (informationUrl is null)
		{
			DiagnosticLog.Write($"[Shared] {stationOperator.Name}: the discovery names no station_information feed");

			return [];
		}

		string informationKey = $"{stationOperator.Id}|station_information";

		GbfsStationInformation? information =
			_information.TryGet(informationKey, out GbfsStationInformation? cachedInformation)
				? cachedInformation
				: await _client.GetInformationAsync(informationUrl, timeout, cancellationToken).ConfigureAwait(false);

		if (information is null)
		{
			// The skeleton is down and nothing of it is cached: the operator's stations vanish for this cycle.
			DiagnosticLog.Write($"[Shared] {stationOperator.Name}: station_information failed; no stations this cycle");

			return [];
		}

		_information.Set(informationKey, information);

		GbfsStationStatus? status = null;
		string statusKey = $"{stationOperator.Id}|station_status";

		if (_status.TryGetValue(statusKey, out (DateTimeOffset At, GbfsStationStatus Value) fresh)
			&& DateTimeOffset.UtcNow - fresh.At < (_statusLifetime.TryGetValue(statusKey, out TimeSpan ttl) ? ttl : TimeSpan.FromSeconds(StatusSeconds)))
		{
			status = fresh.Value;
		}
		else
		{
			if (GbfsClient.FeedUrl(discovery, "station_status") is { } statusUrl)
			{
				status = await _client.GetStatusAsync(statusUrl, timeout, cancellationToken).ConfigureAwait(false);
			}

			if (status is not null)
			{
				// The feed's own ttl, when it asks for less than a minute; at most a minute otherwise.
				TimeSpan lifetime = TimeSpan.FromSeconds(Math.Min(StatusSeconds, Math.Max(1, status.Ttl ?? StatusSeconds)));

				_status[statusKey] = (DateTimeOffset.UtcNow, status);
				_statusLifetime[statusKey] = lifetime;
			}
		}

		return GbfsClient.Join(information, status, stationOperator.Name, stationOperator.Website);
	}
}
