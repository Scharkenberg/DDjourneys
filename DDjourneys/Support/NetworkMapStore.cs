using System.Net.Http;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers.Vvo;
using SkiaSharp;

namespace DDjourneys.Support;

/// <summary>The network map on the device: its file, and whether it is the current one.</summary>
/// <param name="Path">The cached JPG (or PDF) on the device.</param>
/// <param name="Fresh">False when the update failed and the last saved plan is shown instead.</param>
public sealed record NetworkMapFile(string Path, bool Fresh);

/// <summary>
/// The DVB Liniennetzplan on demand: the page is read for the current plan links, the JPG downloaded - a
/// print product that can exceed what a phone can even show, so it is downscaled to 4096 px (the texture
/// limit of many devices) before it is cached - and kept for a week. The PDF is cached as it comes and
/// opens in the system viewer. One download at a time (single flight); a failure keeps the old file,
/// because a stale plan is better than none. All rights to the plan stay with the DVB.
/// </summary>
public sealed class NetworkMapStore(ApiClient api)
{
	private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
	private const int MaxEdge = 4096;
	private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);

	private readonly SemaphoreSlim _gate = new(1, 1);

	private static string Directory => FileSystem.AppDataDirectory;

	private static string JpgPath => Path.Combine(Directory, "networkmap.jpg");

	private static string PdfPath => Path.Combine(Directory, "networkmap.pdf");

	public async Task<NetworkMapFile> GetJpgAsync(CancellationToken cancellationToken = default) =>
		await GetAsync(JpgPath, "image/jpeg", DownscaleAsync, cancellationToken).ConfigureAwait(false);

	public async Task<NetworkMapFile> GetPdfAsync(CancellationToken cancellationToken = default) =>
		await GetAsync(PdfPath, "application/pdf", (_, _) => Task.FromResult(false), cancellationToken).ConfigureAwait(false);

	/// <summary>Fetches the current plan whatever the cache says (the viewer's refresh button).</summary>
	public async Task<NetworkMapFile> RefreshAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			File.Delete(JpgPath);
		}
		catch (Exception)
		{
		}

		return await GetJpgAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<NetworkMapFile> GetAsync(
		string path,
		string accept,
		Func<byte[], CancellationToken, Task<bool>> write,
		CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (Cached(path) is { } file)
			{
				return new NetworkMapFile(file, Fresh: true);
			}

			try
			{
				VvoNetworkMapUrls urls = await FindUrlsAsync(cancellationToken).ConfigureAwait(false);
				Uri? link = path == JpgPath ? urls.Jpg : urls.Pdf;

				if (link is null)
				{
					throw new ApiException("The DVB page names no network map.", (int)System.Net.HttpStatusCode.NotFound);
				}

				(byte[] content, string? mediaType) = await api
					.GetBytesAsync(link.ToString(), accept, DownloadTimeout, cancellationToken)
					.ConfigureAwait(false);

				// The magic bytes decide, not the server's word.
				bool jpeg = content.Length > 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
				bool pdf = content.Length > 4 && content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x46 && content[3] == 0x25;

				if (path == JpgPath ? !jpeg : !pdf)
				{
					throw new ApiException($"The network map download is no {(path == JpgPath ? "JPEG" : "PDF")}.", (int)System.Net.HttpStatusCode.OK);
				}

				bool written = await write(content, cancellationToken).ConfigureAwait(false);

				return new NetworkMapFile(written ? path : CacheRaw(path, content), Fresh: true);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException)
			{
				// A plan that is past its week is still a plan: serve it as stale instead of nothing.
				if (File.Exists(path))
				{
					DiagnosticLog.Write($"[Network map] update failed, showing the saved one: {ex.Message}");

					return new NetworkMapFile(path, Fresh: false);
				}

				throw;
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<VvoNetworkMapUrls> FindUrlsAsync(CancellationToken cancellationToken)
	{
		string html = await api
			.GetAsync(VvoNetworkMap.PageUrl, cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		VvoNetworkMapUrls urls = VvoNetworkMap.FromHtml(html);

		return urls.Jpg is null && urls.Pdf is null
			? throw new ApiException("The DVB page names no network map.", (int)System.Net.HttpStatusCode.NotFound)
			: urls;
	}

	/// <summary>Downscales an oversized plan to what devices can show and writes it; false keeps the bytes as they are.</summary>
	private async Task<bool> DownscaleAsync(byte[] content, CancellationToken cancellationToken)
	{
		using SKBitmap? source = SKBitmap.Decode(content);

		if (source is null
			|| (source.Width <= MaxEdge && source.Height <= MaxEdge))
		{
			await File.WriteAllBytesAsync(JpgPath, content, cancellationToken).ConfigureAwait(false);

			return true;
		}

		double scale = Math.Min((double)MaxEdge / source.Width, (double)MaxEdge / source.Height);

		using SKBitmap scaled = source.Resize(
			new SKImageInfo(
				Math.Max(1, (int)Math.Round(source.Width * scale)),
				Math.Max(1, (int)Math.Round(source.Height * scale)),
				source.ColorType,
				source.AlphaType),
			new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

		using SKImage image = SKImage.FromBitmap(scaled);
		using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

		await File.WriteAllBytesAsync(JpgPath, data.ToArray(), cancellationToken).ConfigureAwait(false);

		DiagnosticLog.Write($"[Network map] downscaled {source.Width}x{source.Height} to {scaled.Width}x{scaled.Height}");

		return true;
	}

	private static string CacheRaw(string path, byte[] content)
	{
		File.WriteAllBytes(path, content);

		return path;
	}

	private static string? Cached(string path) =>
		File.Exists(path)
			&& File.GetLastWriteTimeUtc(path) > DateTime.UtcNow - MaxAge
				? path
				: null;
}
