using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>
/// Park &amp; ride sites for the map layer: the whole list in one answer (there are only a few dozen sites),
/// cached for two minutes and asked again at most that often while somebody looks. A failed or empty answer
/// is not kept, so the next look asks again.
/// </summary>
public sealed class ParkingService
{
	private const string Key = "sites";

	private readonly IParkingProvider? _provider;
	private readonly TtlCache<IReadOnlyList<ParkingSite>> _cache = new(TimeSpan.FromMinutes(2), 8);
	private readonly SemaphoreSlim _gate = new(1, 1);

	public ParkingService(IEnumerable<IParkingProvider> providers) =>
		_provider = providers.FirstOrDefault();

	public bool IsAvailable => _provider is not null;

	public async Task<IReadOnlyList<ParkingSite>> GetSitesAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		if (_provider is not { } provider)
		{
			return [];
		}

		if (_cache.TryGet(Key, out IReadOnlyList<ParkingSite>? cached))
		{
			return cached;
		}

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (_cache.TryGet(Key, out cached))
			{
				return cached;
			}

			IReadOnlyList<ParkingSite> sites =
				await provider.GetSitesAsync(timeout, cancellationToken).ConfigureAwait(false);

			if (sites.Count > 0)
			{
				_cache.Set(Key, sites);
			}

			return sites;
		}
		finally
		{
			_gate.Release();
		}
	}
}
