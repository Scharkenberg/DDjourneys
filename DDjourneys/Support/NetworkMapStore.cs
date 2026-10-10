using System.Net.Http;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys.Support;

/// <summary>The network map on the device: its file, and whether it is the current one.</summary>
/// <param name="Path">The cached PDF (or JPG) on the device.</param>
/// <param name="Fresh">False when the update failed and the last saved plan is shown instead.</param>
public sealed record NetworkMapFile(string Path, bool Fresh);

/// <summary>
/// The DVB Liniennetzplan on demand: the page is read for the current plan links and the file downloaded and kept
/// for a week, exactly as it comes (nothing is scaled down). The PDF is the plan the app shows - vector, so it keeps
/// its detail at any zoom (<see cref="PdfPlanTiles"/>) - and the one the system viewer opens; the JPG is only the
/// fallback when the PDF cannot be had or read. One download at a time (single flight); a failure keeps the old
/// file, because a stale plan is better than none. All rights to the plan stay with the DVB.
/// </summary>
public sealed class NetworkMapStore(ApiClient api)
{
	private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
	private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);

	private readonly SemaphoreSlim _gate = new(1, 1);

	private static string Directory => FileSystem.AppDataDirectory;

	private static string JpgPath => Path.Combine(Directory, "networkmap.jpg");

	private static string PdfPath => Path.Combine(Directory, "networkmap.pdf");

	public Task<NetworkMapFile> GetPdfAsync(CancellationToken cancellationToken = default) =>
		GetAsync(pdf: true, force: false, cancellationToken);

	/// <summary>Fetches the current PDF whatever the cache says (the viewer's refresh button).</summary>
	/// <remarks>The saved plan is kept until the new one is in: offline, a refresh shows the old plan as stale instead of nothing.</remarks>
	public Task<NetworkMapFile> RefreshPdfAsync(CancellationToken cancellationToken = default) =>
		GetAsync(pdf: true, force: true, cancellationToken);

	/// <summary>The JPG, for when the PDF cannot be shown.</summary>
	public Task<NetworkMapFile> GetJpgAsync(bool force = false, CancellationToken cancellationToken = default) =>
		GetAsync(pdf: false, force, cancellationToken);

	private async Task<NetworkMapFile> GetAsync(bool pdf, bool force, CancellationToken cancellationToken)
	{
		string path = pdf ? PdfPath : JpgPath;

		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (!force
				&& Cached(path) is { } file)
			{
				return new NetworkMapFile(file, Fresh: true);
			}

			try
			{
				VvoNetworkMapUrls urls = await FindUrlsAsync(cancellationToken).ConfigureAwait(false);
				Uri link = (pdf ? urls.Pdf : urls.Jpg)
					?? throw new ApiException("The DVB page names no network map.", (int)System.Net.HttpStatusCode.NotFound);

				(byte[] content, _) = await api
					.GetBytesAsync(link.ToString(), pdf ? "application/pdf" : "image/jpeg", DownloadTimeout, cancellationToken)
					.ConfigureAwait(false);

				// The magic bytes decide, not the server's word.
				bool valid = pdf
					? content.Length > 4 && content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46
					: content.Length > 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;

				if (!valid)
				{
					throw new ApiException($"The network map download is no {(pdf ? "PDF" : "JPEG")}.", (int)System.Net.HttpStatusCode.OK);
				}

				// Written aside and moved over, so a cut-off write never replaces a good plan.
				string part = path + ".part";

				await File.WriteAllBytesAsync(part, content, cancellationToken).ConfigureAwait(false);
				File.Move(part, path, overwrite: true);

				return new NetworkMapFile(path, Fresh: true);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex) when (ex is ApiException or HttpRequestException or IOException or OperationCanceledException)
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

	private static string? Cached(string path) =>
		File.Exists(path)
			&& File.GetLastWriteTimeUtc(path) > DateTime.UtcNow - MaxAge
				? path
				: null;
}
