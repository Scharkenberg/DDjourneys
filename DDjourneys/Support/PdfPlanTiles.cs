using System.Collections.Concurrent;
using DDjourneys.Core.Diagnostics;
using SkiaSharp;

namespace DDjourneys.Support;

/// <summary>
/// The tiles of a one-page PDF plan, drawn from the vector content for the level and pixel density asked for, so
/// nothing is ever scaled down to a texture limit: the deepest level is 8192 CSS pixels wide (about six times the
/// A2 sheet in points: its smallest print reads large), the tile itself 256 CSS pixels at up to 3x. Rendered tiles are kept on disk per plan
/// version; the newest request is drawn first (a pan or zoom outruns the old ones), several pages draw in parallel.
/// </summary>
public sealed class PdfPlanTiles : IPlanTiles
{
	/// <summary>Width of the deepest level in units (CSS pixels).</summary>
	private const double DeepWidth = 8192;

	/// <summary>Requests beyond this many waiting are given up, oldest first (the page has long moved on).</summary>
	private const int MaxWaiting = 96;

	private const long MaxCacheBytes = 160L * 1024 * 1024;

	private static readonly double[] Levels = [1 / 32d, 1 / 16d, 1 / 8d, 1 / 4d, 1 / 2d, 1];

	private readonly IPdfPage[] _pages;
	private readonly Stack<IPdfPage> _free;
	private readonly string _directory;
	private readonly Lock _gate = new();
	private readonly LinkedList<Job> _waiting = new();
	private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _running = new(StringComparer.Ordinal);
	private readonly CancellationTokenSource _life = new();
	private bool _disposed;

	private PdfPlanTiles(IPdfPage[] pages, string key, string directory)
	{
		_pages = pages;
		_free = new Stack<IPdfPage>(pages);
		_directory = directory;

		double aspect = pages[0].Aspect;

		Spec = new PlanSpec(key, DeepWidth, Math.Round(DeepWidth / aspect), Levels, Retina: true, OverZoom: 0);
	}

	public PlanSpec Spec { get; }

	public string ContentType => "image/png";

	/// <summary>Opens the plan: one renderer per worker (the renderers are single-threaded), the cache of this version.</summary>
	public static async Task<PdfPlanTiles> OpenAsync(string path, string name, CancellationToken cancellationToken)
	{
		string key = $"{name}-{await Task.Run(() => PlanAddress.Fingerprint(path), cancellationToken).ConfigureAwait(false)}";
		string root = Path.Combine(FileSystem.CacheDirectory, "plans");
		string directory = Path.Combine(root, key);

		Directory.CreateDirectory(directory);

		// The renderers read their own copy: the downloaded file stays free to be replaced while the plan is shown
		// (Windows keeps an open PDF locked).
		string source = Path.Combine(directory, "plan.pdf");

		if (!File.Exists(source))
		{
			string part = source + ".part";

			File.Copy(path, part, overwrite: true);
			File.Move(part, source, overwrite: true);
		}

		int workers = Math.Clamp(Environment.ProcessorCount / 2, 1, 3);
		var pages = new List<IPdfPage>(workers);

		try
		{
			for (int i = 0; i < workers; i++)
			{
				pages.Add(await PdfPages.OpenAsync(source, cancellationToken).ConfigureAwait(false));
			}
		}
		catch
		{
			pages.ForEach(page => page.Dispose());

			throw;
		}

		_ = Task.Run(() => Tidy(root, name, key));

		return new PdfPlanTiles([.. pages], key, directory);
	}

	public Task<Stream?> OpenImageAsync(CancellationToken cancellationToken) =>
		Task.FromResult<Stream?>(null);

	public async Task<Stream?> OpenTileAsync(PlanTile tile, CancellationToken cancellationToken)
	{
		if (tile.Level < 0 || tile.Level >= Levels.Length || tile.X < 0 || tile.Y < 0)
		{
			return null;
		}

		string file = Path.Combine(_directory, $"{tile.Level}_{tile.X}_{tile.Y}@{tile.Ratio}.png");

		try
		{
			if (File.Exists(file))
			{
				return File.OpenRead(file);
			}

			// One drawing per tile, however often the page asks for it.
			byte[]? png = await _running
				.GetOrAdd(file, path => new Lazy<Task<byte[]?>>(() => DrawAsync(tile, path)))
				.Value
				.WaitAsync(cancellationToken)
				.ConfigureAwait(false);

			return png is null ? null : new MemoryStream(png, writable: false);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
		{
			return null;
		}
	}

	private async Task<byte[]?> DrawAsync(PlanTile tile, string file)
	{
		try
		{
			var job = new Job(tile, new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously));

			Enqueue(job);

			byte[]? png = await job.Done.Task.ConfigureAwait(false);

			if (png is not null)
			{
				string part = file + ".part";

				await File.WriteAllBytesAsync(part, png).ConfigureAwait(false);
				File.Move(part, file, overwrite: true);
			}

			return png;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Not kept on disk is no reason not to show it: the drawing is lost, not the tile.
			return null;
		}
		finally
		{
			_running.TryRemove(file, out _);
		}
	}

	private void Enqueue(Job job)
	{
		IPdfPage? page = null;
		Job? dropped = null;

		lock (_gate)
		{
			if (_disposed)
			{
				job.Done.TrySetResult(null);

				return;
			}

			_waiting.AddFirst(job);

			if (_waiting.Count > MaxWaiting)
			{
				dropped = _waiting.Last!.Value;
				_waiting.RemoveLast();
			}

			_free.TryPop(out page);
		}

		dropped?.Done.TrySetResult(null);

		if (page is not null)
		{
			_ = Task.Run(() => WorkAsync(page));
		}
	}

	/// <summary>One worker per page: draws the newest waiting tile until none is left, then gives the page back.</summary>
	private async Task WorkAsync(IPdfPage page)
	{
		while (true)
		{
			Job job;

			lock (_gate)
			{
				if (_disposed || _waiting.Count == 0)
				{
					_free.Push(page);

					return;
				}

				job = _waiting.First!.Value;
				_waiting.RemoveFirst();
			}

			try
			{
				job.Done.TrySetResult(await RenderAsync(page, job.Tile, _life.Token).ConfigureAwait(false));
			}
			catch (Exception ex)
			{
				if (ex is not OperationCanceledException and not ObjectDisposedException)
				{
					DiagnosticLog.Write($"[Plan] tile {job.Tile.Level}/{job.Tile.X}/{job.Tile.Y} failed: {ex.Message}");
				}

				job.Done.TrySetResult(null);
			}
		}
	}

	/// <summary>
	/// Draws a tile. Tiles at the right and bottom edge reach past the sheet: only the part on it is drawn, then put
	/// into a transparent tile, so the paper edge stays where the plan ends.
	/// </summary>
	private async Task<byte[]?> RenderAsync(IPdfPage page, PlanTile tile, CancellationToken cancellationToken)
	{
		(double levelWidth, double levelHeight) = Spec.LevelSize(tile.Level);

		int size = Spec.TileSize;
		double x0 = (double)tile.X * size;
		double y0 = (double)tile.Y * size;
		double x1 = Math.Min(x0 + size, levelWidth);
		double y1 = Math.Min(y0 + size, levelHeight);

		if (x1 <= x0 || y1 <= y0)
		{
			return null;
		}

		int full = size * tile.Ratio;
		int pixelWidth = Math.Max(1, (int)Math.Round((x1 - x0) * tile.Ratio));
		int pixelHeight = Math.Max(1, (int)Math.Round((y1 - y0) * tile.Ratio));

		byte[] png = await page.RenderPngAsync(
			x0 / levelWidth,
			y0 / levelHeight,
			(x1 - x0) / levelWidth,
			(y1 - y0) / levelHeight,
			pixelWidth,
			pixelHeight,
			cancellationToken).ConfigureAwait(false);

		return pixelWidth == full && pixelHeight == full
			? png
			: Pad(png, full);
	}

	private static byte[] Pad(byte[] part, int size)
	{
		using SKBitmap source = SKBitmap.Decode(part) ?? throw new InvalidDataException("The rendered tile could not be read.");
		using var tile = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));

		using (var canvas = new SKCanvas(tile))
		{
			canvas.Clear(SKColors.Transparent);
			canvas.DrawBitmap(source, 0, 0);
		}

		using SKData data = tile.Encode(SKEncodedImageFormat.Png, 100);

		return data.ToArray();
	}

	/// <summary>Drops the tiles of older versions of this plan and keeps the current one within its budget, oldest out first.</summary>
	private static void Tidy(string root, string name, string key)
	{
		try
		{
			foreach (string directory in Directory.EnumerateDirectories(root, $"{name}-*"))
			{
				if (!string.Equals(Path.GetFileName(directory), key, StringComparison.Ordinal))
				{
					Directory.Delete(directory, recursive: true);
				}
			}

			FileInfo[] files = new DirectoryInfo(Path.Combine(root, key)).GetFiles("*.png");
			long total = files.Sum(file => file.Length);

			foreach (FileInfo file in files.OrderBy(file => file.LastWriteTimeUtc))
			{
				if (total <= MaxCacheBytes)
				{
					break;
				}

				total -= file.Length;
				file.Delete();
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			DiagnosticLog.Write($"[Plan] cache not tidied: {ex.Message}");
		}
	}

	public void Dispose()
	{
		List<Job> waiting;

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			waiting = [.. _waiting];
			_waiting.Clear();
		}

		_life.Cancel();
		waiting.ForEach(job => job.Done.TrySetResult(null));

		// A page that is drawing finishes under its own lock first.
		foreach (IPdfPage page in _pages)
		{
			page.Dispose();
		}
	}

	private sealed record Job(PlanTile Tile, TaskCompletionSource<byte[]?> Done);
}
