using System.Net.Http;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Mapping;

namespace DDjourneys.Support;

/// <summary>How a city-map download went: what was fetched, what failed, why it stopped.</summary>
public sealed record CityTilesDownload(
	bool Skipped,
	bool Cancelled,
	int Fetched,
	int Failed,
	int Existing,
	int Total);

/// <summary>
/// The offline city map on the device: the raster tiles of the Leaflet engine for the Dresden box
/// (<see cref="CityTiles"/>), downloaded once on demand (Settings, Offline) and served to the map page when a
/// network tile fails. The tiles are the SAME public ones the page loads (OpenStreetMap first, CARTO's raster
/// fallback — see <see cref="InterfaceSchemas.OsmTileUrl"/>); nothing is re-served or recoloured, dark mode
/// keeps the page's own CSS inversion. Files live under <c>Cache/city-tiles/{z}/{x}/{y}.png</c>, plain 256 px
/// tiles (no retina doubling — the cache holds one tile per address whatever screen asks for it). The download
/// runs two requests at a time, shallow levels first, and only ever because the person pressed the button
/// (the OSM tile usage policy: light, polite use). A size cap of 60 MB — about three honest downloads of the
/// whole set, a comfort budget next to the plan cache's 160 MB — is kept by <see cref="TidyAsync"/>, which
/// drops the deepest zoom levels first: the overview survives, the sharpest detail is refetched. A download
/// can be limited to unmetered connections (the caller's choice, on by default in the settings).
/// </summary>
public sealed class CityTilesStore(ApiClient api)
{
	/// <summary>Everything above this is tidied away, deepest zoom levels first.</summary>
	public const long MaxCacheBytes = 60L * 1024 * 1024;

	private const int AtATime = 2;

	/// <summary>After this many failed OSM tiles the rest of the run comes from CARTO's raster (needs the key).</summary>
	private const int GiveUpOnOsmAfter = 8;

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

	private static string Root => Path.Combine(FileSystem.CacheDirectory, "city-tiles");

	/// <summary>The bytes of a tile, or null when the cache holds none. Only tiles of the Dresden set are kept.</summary>
	public async Task<byte[]?> ReadAsync(int zoom, int x, int y, CancellationToken cancellationToken = default)
	{
		if (!CityTiles.Within(zoom, x, y))
		{
			return null;
		}

		try
		{
			return await File.ReadAllBytesAsync(FileOf(zoom, x, y), cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
		{
			return null;
		}
	}

	/// <summary>How much of the city map is on the device.</summary>
	public async Task<(long Bytes, int Tiles)> MeasureAsync(CancellationToken cancellationToken = default)
	{
		long bytes = 0;
		int tiles = 0;

		try
		{
			foreach (string file in Directory.EnumerateFiles(Root, "*.png", SearchOption.AllDirectories))
			{
				cancellationToken.ThrowIfCancellationRequested();

				FileInfo info = new(file);

				bytes += info.Length;
				tiles++;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			DiagnosticLog.Write($"[City tiles] measuring the cache failed: {ex.Message}");
		}

		return (bytes, tiles);
	}

	/// <summary>
	/// Downloads the tiles that are missing, shallow levels first, a few at a time. The source ladder is per
	/// run: OpenStreetMap first, and only when its first answers all fail does the rest of the run come from
	/// CARTO's raster tiles (they need the user's key; without one the run stays with OSM and counts its
	/// failures). A tile that fails on the run's source is counted, not thrown: the map shows what arrived.
	/// </summary>
	/// <param name="progress">Reports how many tiles of the run are settled (fetched, failed or already there).</param>
	/// <param name="wifiOnly">Skip the whole run when the connection is metered.</param>
	/// <param name="cartoKey">The user's CARTO key, for the raster fallback source (may be empty).</param>
	public async Task<CityTilesDownload> PrefetchAsync(
		IProgress<int>? progress,
		bool wifiOnly,
		string cartoKey,
		CancellationToken cancellationToken = default)
	{
		if (wifiOnly && !Unmetered())
		{
			DiagnosticLog.Write("[City tiles] download skipped: the connection is metered");

			return new CityTilesDownload(Skipped: true, Cancelled: false, 0, 0, 0, CityTiles.Count());
		}

		var counters = new Counters();
		var gate = new SemaphoreSlim(AtATime, AtATime);
		bool keySet = !string.IsNullOrWhiteSpace(cartoKey);

		try
		{
			await Task.WhenAll(
					CityTiles.Tiles()
						.Select(
							async tile =>
							{
								await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

								try
								{
									await FetchAsync(tile, keySet, cartoKey, counters, progress, cancellationToken).ConfigureAwait(false);
								}
								finally
								{
									gate.Release();
								}
							}))
					.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			counters.Cancelled = true;
		}
		catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or UnauthorizedAccessException)
		{
			DiagnosticLog.Write($"[City tiles] download stopped: {ex.Message}");

			counters.Cancelled = true;
		}

		await TidyAsync(CancellationToken.None).ConfigureAwait(false);

		(long bytes, _) = await MeasureAsync(CancellationToken.None).ConfigureAwait(false);

		DiagnosticLog.Write(
			$"[City tiles] download {(counters.Cancelled ? "stopped" : "done")}: {counters.Fetched} fetched, {counters.Failed} failed, {counters.Existing} already there, {bytes / 1024} KiB on disk");

		return new CityTilesDownload(false, counters.Cancelled, counters.Fetched, counters.Failed, counters.Existing, CityTiles.Count());
	}

	/// <summary>Deletes the whole city map from the device.</summary>
	public Task RemoveAsync()
	{
		try
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}

			DiagnosticLog.Write("[City tiles] cache removed");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			DiagnosticLog.Write($"[City tiles] removing the cache failed: {ex.Message}");
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Keeps the cache within its budget by deleting the deepest zoom levels first: the overview stays, the
	/// sharpest detail (the heaviest part of the set) is what a later download refetches.
	/// </summary>
	public async Task TidyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			(long bytes, _) = await MeasureAsync(cancellationToken).ConfigureAwait(false);

			for (int zoom = CityTiles.MaxZoom; zoom > CityTiles.MinZoom && bytes > MaxCacheBytes; zoom--)
			{
				string level = Path.Combine(Root, zoom.ToString(System.Globalization.CultureInfo.InvariantCulture));

				if (!Directory.Exists(level))
				{
					continue;
				}

				foreach (string file in Directory.EnumerateFiles(level, "*.png", SearchOption.AllDirectories))
				{
					cancellationToken.ThrowIfCancellationRequested();

					try
					{
						FileInfo info = new(file);
						bytes -= info.Length;
						info.Delete();
					}
					catch (IOException)
					{
					}
				}
			}

			if (bytes > MaxCacheBytes)
			{
				DiagnosticLog.Write($"[City tiles] cache over budget even after the deepest levels: {bytes / 1024} KiB");
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			DiagnosticLog.Write($"[City tiles] tidying the cache failed: {ex.Message}");
		}
	}

	private async Task FetchAsync(
		(int Zoom, int X, int Y) tile,
		bool keySet,
		string cartoKey,
		Counters counters,
		IProgress<int>? progress,
		CancellationToken cancellationToken)
	{
		string file = FileOf(tile.Zoom, tile.X, tile.Y);

		if (File.Exists(file))
		{
			System.Threading.Interlocked.Increment(ref counters.Existing);
		}
		else
		{
			// The ladder moves one way per run: OSM while its failures stay few, CARTO raster after (only with
			// a key — without one the run keeps asking OSM and counts what it does not get).
			bool fromOsm = !keySet || Volatile.Read(ref counters.Failed) < GiveUpOnOsmAfter;

			if (await DownloadAsync(tile, fromOsm, cartoKey, cancellationToken).ConfigureAwait(false))
			{
				System.Threading.Interlocked.Increment(ref counters.Fetched);
			}
			else
			{
				System.Threading.Interlocked.Increment(ref counters.Failed);
			}
		}

		progress?.Report(System.Threading.Interlocked.Increment(ref counters.Settled));
	}

	private async Task<bool> DownloadAsync((int Zoom, int X, int Y) tile, bool fromOsm, string cartoKey, CancellationToken cancellationToken)
	{
		string url = fromOsm
			? $"{InterfaceSchemas.OsmTileUrl}{tile.Zoom}/{tile.X}/{tile.Y}.png"
			: $"https://basemaps.cartocdn.com/rastertiles/voyager/{tile.Zoom}/{tile.X}/{tile.Y}.png?key={Uri.EscapeDataString(cartoKey)}";

		try
		{
			(byte[] content, _) = await api
				.GetBytesAsync(url, "image/png", Timeout, cancellationToken)
				.ConfigureAwait(false);

			// The magic bytes decide, not the server's word (an error page with status 200 is not a tile).
			if (content.Length < 8 || content[0] != 0x89 || content[1] != 0x50 || content[2] != 0x4E || content[3] != 0x47)
			{
				return false;
			}

			string file = FileOf(tile.Zoom, tile.X, tile.Y);
			string part = file + ".part";

			Directory.CreateDirectory(Path.GetDirectoryName(file)!);
			await File.WriteAllBytesAsync(part, content, cancellationToken).ConfigureAwait(false);
			File.Move(part, file, overwrite: true);

			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool Unmetered() =>
		Connectivity.Current.NetworkAccess == NetworkAccess.Internet
		&& Connectivity.Current.ConnectionProfiles.Any(profile => profile is ConnectionProfile.WiFi or ConnectionProfile.Ethernet);

	private static string FileOf(int zoom, int x, int y) =>
		Path.Combine(
			Root,
			zoom.ToString(System.Globalization.CultureInfo.InvariantCulture),
			x.ToString(System.Globalization.CultureInfo.InvariantCulture),
			y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");

	private sealed class Counters
	{
		public int Fetched;

		public int Failed;

		public int Existing;

		public int Settled;

		public bool Cancelled;
	}
}
