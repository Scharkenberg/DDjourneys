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

	/// <summary>A file, not a setting: Windows refuses a setting value above a few KB, and 30 boards are more than that.</summary>
	private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "boards.cache.json");

	/// <summary>A stop's board is kept at most this often: the page refreshes every 30 s, and every keep rewrites the whole list.</summary>
	private static readonly TimeSpan KeepEvery = TimeSpan.FromMinutes(2);

	private static readonly Dictionary<string, DateTimeOffset> Kept = [];
	private static readonly Lock Gate = new();

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

		string slot = $"{providerId}|{stop.Id}|{isArrival}";

		lock (Gate)
		{
			if (Kept.TryGetValue(slot, out DateTimeOffset last) && DateTimeOffset.UtcNow - last < KeepEvery)
			{
				return;
			}

			Kept[slot] = DateTimeOffset.UtcNow;
		}

		try
		{
			string json =
				BoardCacheList.Write(
					BoardCacheList.Touch(
						Load(),
						new CachedBoard(providerId, stop.Id, isArrival, DateTimeOffset.UtcNow, departures)));

			// Written aside and moved over, so a cut-off write never replaces a good file.
			string part = FilePath + ".part";

			File.WriteAllText(part, json);
			File.Move(part, FilePath, overwrite: true);
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
			if (File.Exists(FilePath))
			{
				return BoardCacheList.Parse(File.ReadAllText(FilePath));
			}

			// Older builds kept the list in the preferences, which Windows limits to a few KB: it moves to the file.
			string old = Preferences.Default.Get(Key, string.Empty);

			if (old.Length > 0)
			{
				Preferences.Default.Remove(Key);
			}

			return BoardCacheList.Parse(old);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Reading the kept boards failed: {ex.Message}");

			return [];
		}
	}
}
