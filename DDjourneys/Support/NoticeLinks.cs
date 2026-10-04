using System.Net.Http.Headers;
using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>What a link of a notice leads to.</summary>
public enum NoticeLinkKind
{
	/// <summary>Anything the system browser should show (web pages, mail, phone numbers).</summary>
	Other,

	Pdf,

	Image
}


/// <summary>
/// Decides what to do with a link of a notice by asking the server what it is, not by guessing from the address:
/// one request (GET, headers first) gives the content type; a missing or generic type (octet-stream) is settled by
/// the first bytes of the body. A PDF is downloaded and handed to the system's viewer, a picture is shown in the
/// app (the notice page's image viewer), everything else goes to the system browser. When the server cannot be
/// asked, the file extension decides.
/// </summary>
public static class NoticeLinks
{
	private const long MaxPdfBytes = 50L * 1024 * 1024;

	private static readonly HttpClient Http = CreateClient();

	private static HttpClient CreateClient()
	{
		var client =
			new HttpClient(
				new SocketsHttpHandler
				{
					AllowAutoRedirect = true,
					MaxAutomaticRedirections = 6,
					ConnectTimeout = TimeSpan.FromSeconds(10)
				})
			{
				Timeout = TimeSpan.FromSeconds(60)
			};

		client.DefaultRequestHeaders.UserAgent.ParseAdd("DDjourneys/1.0 (+https://github.com/Scharkenberg/DDjourneys)");
		client.DefaultRequestHeaders.Accept.ParseAdd("*/*");

		return client;
	}

	/// <summary>
	/// Opens the link in the way its content calls for. Returns null on success, otherwise a short reason for
	/// the passenger. <paramref name="showImage"/> displays a picture inside the app.
	/// </summary>
	public static async Task<string?> OpenAsync(
		Uri uri,
		Func<Uri, Task> showImage,
		string failedText,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(uri);
		ArgumentNullException.ThrowIfNull(showImage);

		try
		{
			if (uri.Scheme is not ("http" or "https"))
			{
				await Launcher.Default.OpenAsync(uri);

				return null;
			}

			using var request = new HttpRequestMessage(HttpMethod.Get, uri);

			using HttpResponseMessage response =
				await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

			string? type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();

			DiagnosticLog.Write(
				$"[Notice link] {uri} -> {(int)response.StatusCode} {type ?? "(no type)"} " +
				$"length={response.Content.Headers.ContentLength?.ToString() ?? "?"} " +
				$"disposition={response.Content.Headers.ContentDisposition?.ToString() ?? "-"}");

			if (!response.IsSuccessStatusCode)
			{
				// Some servers refuse the request (or the app's address) but the browser may still manage.
				await Launcher.Default.OpenAsync(uri);

				return null;
			}

			NoticeLinkKind kind = KindOf(type);

			bool generic = type is null or "application/octet-stream" or "binary/octet-stream";

			if (kind == NoticeLinkKind.Image)
			{
				await showImage(uri);

				return null;
			}

			if (kind == NoticeLinkKind.Pdf
				|| generic)
			{
				byte[] bytes = await ReadBodyAsync(response, cancellationToken);

				switch (Sniff(bytes))
				{
					case NoticeLinkKind.Pdf:
						await OpenPdfAsync(uri, response, bytes);

						return null;

					case NoticeLinkKind.Image:
						await showImage(uri);

						return null;
				}
			}

			// Web pages and anything the app has no view for.
			await Launcher.Default.OpenAsync(uri);

			return null;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Notice link] {uri} failed: {ex.GetType().Name}: {ex.Message}");

			try
			{
				// The request failed (offline, blocked): by its extension a picture can still be tried in the
				// viewer; everything else is left to the system browser, which has its own chance.
				if (KindOfExtension(uri.AbsolutePath, null) == NoticeLinkKind.Image)
				{
					await showImage(uri);
				}
				else
				{
					await Launcher.Default.OpenAsync(uri);
				}

				return null;
			}
			catch
			{
				return failedText;
			}
		}
	}

	private static NoticeLinkKind KindOf(string? mediaType) =>
		mediaType switch
		{
			"application/pdf" or "application/x-pdf" => NoticeLinkKind.Pdf,
			not null when mediaType.StartsWith("image/", StringComparison.Ordinal)
				&& mediaType != "image/svg+xml" => NoticeLinkKind.Image,
			_ => NoticeLinkKind.Other
		};

	private static NoticeLinkKind KindOfExtension(string path, string? fileName)
	{
		string name = (fileName ?? path).Trim('"').ToLowerInvariant();

		if (name.EndsWith(".pdf", StringComparison.Ordinal))
		{
			return NoticeLinkKind.Pdf;
		}

		return name.EndsWith(".png", StringComparison.Ordinal)
			|| name.EndsWith(".jpg", StringComparison.Ordinal)
			|| name.EndsWith(".jpeg", StringComparison.Ordinal)
			|| name.EndsWith(".gif", StringComparison.Ordinal)
			|| name.EndsWith(".webp", StringComparison.Ordinal)
			|| name.EndsWith(".bmp", StringComparison.Ordinal)
				? NoticeLinkKind.Image
				: NoticeLinkKind.Other;
	}

	/// <summary>Content by its first bytes (magic numbers); null when unknown.</summary>
	private static NoticeLinkKind? Sniff(byte[] bytes)
	{
		static bool Starts(byte[] data, params byte[] prefix) =>
			data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

		if (Starts(bytes, 0x25, 0x50, 0x44, 0x46, 0x2D))
		{
			return NoticeLinkKind.Pdf;
		}

		if (Starts(bytes, 0x89, 0x50, 0x4E, 0x47)
			|| Starts(bytes, 0xFF, 0xD8, 0xFF)
			|| Starts(bytes, 0x47, 0x49, 0x46, 0x38)
			|| (Starts(bytes, 0x52, 0x49, 0x46, 0x46) && bytes.Length > 11 && bytes[8] == 0x57 && bytes[9] == 0x45))
		{
			return NoticeLinkKind.Image;
		}

		return null;
	}

	private static async Task<byte[]> ReadBodyAsync(
		HttpResponseMessage response,
		CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is { } length
			&& length > MaxPdfBytes)
		{
			throw new InvalidOperationException("The file is too large.");
		}

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using var buffer = new MemoryStream();

		byte[] chunk = new byte[81920];
		int read;

		while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
		{
			buffer.Write(chunk, 0, read);

			if (buffer.Length > MaxPdfBytes)
			{
				throw new InvalidOperationException("The file is too large.");
			}
		}

		return buffer.ToArray();
	}

	private static async Task OpenPdfAsync(
		Uri uri,
		HttpResponseMessage response,
		byte[] bytes)
	{
		string name =
			response.Content.Headers.ContentDisposition?.FileNameStar
			?? response.Content.Headers.ContentDisposition?.FileName
			?? Path.GetFileName(uri.AbsolutePath);

		name = new string([.. name.Trim('"').Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);

		if (name.Length == 0)
		{
			name = "notice";
		}

		if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
		{
			name += ".pdf";
		}

		string path = Path.Combine(FileSystem.CacheDirectory, name);

		await File.WriteAllBytesAsync(path, bytes);

		await Launcher.Default.OpenAsync(
			new OpenFileRequest(
				name,
				new ReadOnlyFile(path, "application/pdf")));
	}
}
