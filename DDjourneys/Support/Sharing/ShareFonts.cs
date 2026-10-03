using SkiaSharp;

namespace DDjourneys.Support.Sharing;

/// <summary>
/// The typefaces of the share picture: the font face chosen under Appearance, loaded from the app package
/// (the same files the UI uses), or the platform's own sans-serif for "System". Characters a face lacks
/// are drawn with a fallback face found per character, so nothing turns into a blank box.
/// </summary>
internal sealed class ShareFonts
{
	private static readonly Dictionary<string, SKTypeface> Loaded = new(StringComparer.Ordinal);
	private static readonly SemaphoreSlim Gate = new(1, 1);

	private readonly Dictionary<(SKTypeface, int), SKTypeface> _fallbacks = [];

	private ShareFonts(SKTypeface regular, SKTypeface semibold)
	{
		Regular = regular;
		Semibold = semibold;
	}

	public SKTypeface Regular { get; }

	public SKTypeface Semibold { get; }

	public static async Task<ShareFonts> LoadAsync(string fontFace)
	{
		(string? regularFile, string? semiboldFile) =
			FontCatalog.Normalize(fontFace) switch
			{
				FontCatalog.InterTightId => ("InterTight-Regular.ttf", "InterTight-SemiBold.ttf"),
				FontCatalog.OpenSansId => ("OpenSans-Regular.ttf", "OpenSans-Semibold.ttf"),
				_ => ((string?)null, (string?)null)
			};

		SKTypeface regular = await FromPackageAsync(regularFile) ?? SystemFace(SKFontStyleWeight.Normal);
		SKTypeface semibold = await FromPackageAsync(semiboldFile) ?? SystemFace(SKFontStyleWeight.SemiBold);

		return new ShareFonts(regular, semibold);
	}

	/// <summary>The face that can draw <paramref name="codepoint"/>: the preferred one, or a matching system face.</summary>
	public SKTypeface For(SKTypeface preferred, int codepoint)
	{
		if (codepoint < 0x80 || preferred.ContainsGlyph(codepoint))
		{
			return preferred;
		}

		if (_fallbacks.TryGetValue((preferred, codepoint), out SKTypeface? known))
		{
			return known;
		}

		SKTypeface? match = null;

		try
		{
			match = SKFontManager.Default.MatchCharacter(
				preferred.FamilyName,
				preferred.FontStyle,
				null,
				codepoint);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"No fallback face for U+{codepoint:X4}: {ex.Message}");
		}

		SKTypeface result = match ?? preferred;
		_fallbacks[(preferred, codepoint)] = result;

		return result;
	}

	private static async Task<SKTypeface?> FromPackageAsync(string? file)
	{
		if (file is null)
		{
			return null;
		}

		await Gate.WaitAsync();

		try
		{
			if (Loaded.TryGetValue(file, out SKTypeface? cached))
			{
				return cached;
			}

			// MauiFont files are part of the app package and can be opened by file name.
			await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(file);
			using var copy = new MemoryStream();
			await stream.CopyToAsync(copy);

			SKTypeface? typeface = SKTypeface.FromData(SKData.CreateCopy(copy.ToArray()));

			if (typeface is not null)
			{
				Loaded[file] = typeface;
			}

			return typeface;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Share font {file} not loaded: {ex.Message}");

			return null;
		}
		finally
		{
			Gate.Release();
		}
	}

	private static SKTypeface SystemFace(SKFontStyleWeight weight)
	{
		string family =
#if ANDROID
			"sans-serif";
#elif WINDOWS
			"Segoe UI";
#else
			string.Empty;
#endif

		return SKTypeface.FromFamilyName(family, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
			?? SKTypeface.Default;
	}
}
