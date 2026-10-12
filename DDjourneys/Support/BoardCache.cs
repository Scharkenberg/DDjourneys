using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support;

/// <summary>
/// The last boards of the stops on the device (key <c>boards.cache</c>): the departures page shows them with an
/// as-of stamp when a fetch fails, and a widget board pass writes the same snapshots, so the stops a person
/// looks at answer offline. The list itself is <see cref="BoardCacheList"/> (pure, unit-tested); this is only the
/// storage access, provider-scoped like the place store.
/// </summary>
public static class BoardCache
{
	private const string Key = "boards.cache";

	/// <summary>Keeps this stop's board as the newest of the cache (LRU bound, see <see cref="BoardCacheList.Capacity"/>).</summary>
	public static void Store(
		string providerId,
		Location stop,
		bool isArrival,
		IReadOnlyList<Departure> departures)
	{
		ArgumentNullException.ThrowIfNull(stop);
		ArgumentNullException.ThrowIfNull(departures);

		if (departures.Count == 0 || string.IsNullOrWhiteSpace(stop.Id))
		{
			return;
		}

		try
		{
			Preferences.Default.Set(
				Key,
				BoardCacheList.Write(
					BoardCacheList.Touch(
						Load(),
						new CachedBoard(providerId, stop.Id, isArrival, DateTimeOffset.UtcNow, departures))));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Keeping the board of {stop.Name} failed: {ex.Message}");
		}
	}

	public static CachedBoard? Find(string providerId, string stopId, bool isArrival) =>
		BoardCacheList.Find(Load(), providerId, stopId, isArrival);

	private static IReadOnlyList<CachedBoard> Load()
	{
		try
		{
			return BoardCacheList.Parse(Preferences.Default.Get(Key, string.Empty));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Reading the kept boards failed: {ex.Message}");

			return [];
		}
	}
}
