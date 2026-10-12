using System.Collections.Concurrent;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// The tiles of a plan someone else has cut (the VVO plans), fetched on demand and kept on disk per plan edition, so
/// a plan once seen works offline. A tile the server does not have (the blank margin) is answered with a transparent
/// pixel and remembered, never asked again. After the first view the whole pyramid is fetched in the background on
/// an unmetered connection (the shallowest level always), a few requests at a time behind the page's own.
/// </summary>
public sealed partial class RemotePlanTiles : IPlanTiles
{
	private const int PageRequests = 6;
	private const int BackgroundRequests = 2;

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

	/// <summary>A 1x1 transparent PNG: what a missing tile is.</summary>
	private static readonly byte[] Blank = Convert.FromBase64String(
		"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

	private readonly ApiClient _api;
	private readonly Func<int, int, int, string> _url;
	private readonly string _directory;
	private readonly SemaphoreSlim _page = new(PageRequests, PageRequests);
	private readonly SemaphoreSlim _background = new(BackgroundRequests, BackgroundRequests);
	private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _running = new(StringComparer.Ordinal);
	private readonly CancellationTokenSource _life = new();
	private int _prefetching;

	public RemotePlanTiles(ApiClient api, PlanSpec spec, string directory, Func<int, int, int, string> url)
	{
		_api = api;
		_url = url;
		_directory = directory;
		Spec = spec;

		Directory.CreateDirectory(directory);
	}

	public PlanSpec Spec { get; }

	public string ContentType => "image/png";

	public Task<Stream?> OpenImageAsync(CancellationToken cancellationToken) =>
		Task.FromResult<Stream?>(null);

	public async Task<Stream?> OpenTileAsync(PlanTile tile, CancellationToken cancellationToken)
	{
		if (!Exists(tile.Level, tile.X, tile.Y))
		{
			return new MemoryStream(Blank, writable: false);
		}

		byte[]? png = await GetAsync(tile.Level, tile.X, tile.Y, _page, cancellationToken).ConfigureAwait(false);

		return png is null ? null : new MemoryStream(png, writable: false);
	}

	/// <summary>
	/// Fetches what is not on disk yet, shallow levels first, without crowding the page: everything on Wi-Fi or a
	/// cable, only the overview level on a metered connection. Once per plan; stops when the plan is closed.
	/// </summary>
	public void Prefetch()
	{
		if (Interlocked.Exchange(ref _prefetching, 1) == 1)
		{
			return;
		}

		bool unmetered =
			Connectivity.Current.NetworkAccess == NetworkAccess.Internet
			&& Connectivity.Current.ConnectionProfiles.Any(profile => profile is ConnectionProfile.WiFi or ConnectionProfile.Ethernet);

		int deepest = unmetered ? Spec.Scales.Count - 1 : Spec.MinNativeLevel;
		CancellationToken cancellationToken = _life.Token;

		_ = Task.Run(
			async () =>
			{
				int fetched = 0;

				try
				{
					for (int level = Spec.MinNativeLevel; level <= deepest; level++)
					{
						(int columns, int rows) = Grid(level);

						for (int y = 0; y < rows; y++)
						{
							for (int x = 0; x < columns; x++)
							{
								cancellationToken.ThrowIfCancellationRequested();

								if (!File.Exists(FileOf(level, x, y)) && !File.Exists(MissingOf(level, x, y)))
								{
									await GetAsync(level, x, y, _background, cancellationToken).ConfigureAwait(false);
									fetched++;
								}
							}
						}
					}

					DiagnosticLog.Write($"[Plan] {Spec.Key}: {fetched} tiles fetched for offline use (levels {Spec.MinNativeLevel}-{deepest})");
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					DiagnosticLog.Write($"[Plan] {Spec.Key}: offline fetch stopped after {fetched} tiles: {ex.Message}");
				}
			},
			cancellationToken);
	}

	private async Task<byte[]?> GetAsync(int level, int x, int y, SemaphoreSlim slots, CancellationToken cancellationToken)
	{
		string file = FileOf(level, x, y);

		try
		{
			if (File.Exists(file))
			{
				return await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
			}

			if (File.Exists(MissingOf(level, x, y)))
			{
				return Blank;
			}

			// One download per tile, whoever asks.
			return await _running
				.GetOrAdd(file, _ => new Lazy<Task<byte[]?>>(() => DownloadAsync(level, x, y, slots)))
				.Value
				.WaitAsync(cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
		{
			return null;
		}
	}

	private async Task<byte[]?> DownloadAsync(int level, int x, int y, SemaphoreSlim slots)
	{
		string file = FileOf(level, x, y);
		CancellationToken cancellationToken = _life.Token;

		try
		{
			await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

			try
			{
				(byte[] content, _) = await _api
					.GetBytesAsync(_url(level, x, y), "image/png", Timeout, cancellationToken)
					.ConfigureAwait(false);

				// The magic bytes decide: an error page with status 200 is not a tile.
				if (content.Length < 8 || content[0] != 0x89 || content[1] != 0x50 || content[2] != 0x4E || content[3] != 0x47)
				{
					return null;
				}

				string part = file + ".part";

				await File.WriteAllBytesAsync(part, content, cancellationToken).ConfigureAwait(false);
				File.Move(part, file, overwrite: true);

				return content;
			}
			finally
			{
				slots.Release();
			}
		}
		catch (ApiException ex) when (ex.StatusCode == 404)
		{
			// Blank margin: the server keeps no tile there.
			await File.WriteAllBytesAsync(MissingOf(level, x, y), [], CancellationToken.None).ConfigureAwait(false);

			return Blank;
		}
		catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
		{
			return null;
		}
		finally
		{
			_running.TryRemove(file, out _);
		}
	}

	/// <summary>Columns and rows of a level that touch the plan.</summary>
	private (int Columns, int Rows) Grid(int level)
	{
		(double width, double height) = Spec.LevelSize(level);

		return ((int)Math.Ceiling(width / Spec.TileSize), (int)Math.Ceiling(height / Spec.TileSize));
	}

	private bool Exists(int level, int x, int y)
	{
		if (level < Spec.MinNativeLevel || level >= Spec.Scales.Count || x < 0 || y < 0)
		{
			return false;
		}

		(int columns, int rows) = Grid(level);

		return x < columns && y < rows;
	}

	private string FileOf(int level, int x, int y) =>
		Path.Combine(_directory, $"{level}_{x}_{y}.png");

	private string MissingOf(int level, int x, int y) =>
		Path.Combine(_directory, $"{level}_{x}_{y}.none");

	public void Dispose() =>
		_life.Cancel();
}
