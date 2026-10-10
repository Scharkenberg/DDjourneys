using System.Text.RegularExpressions;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>The links of the current standard network map, as far as the page names them.</summary>
public sealed record VvoNetworkMapUrls(Uri? Jpg, Uri? Pdf);

/// <summary>
/// Reads the DVB Liniennetzplan page for the current standard plan (JPG and PDF). Only the standard plan
/// counts: file names like dvb_lnp_2_st_1_jpg.jpg; city-centre excerpts and other specials are ignored.
/// The page's structure is DVB's to change - a match that fails is a friendly error, never a crash.
/// </summary>
public static partial class VvoNetworkMap
{
	public const string PageUrl = "https://www.dvb.de/de-de/liniennetz/liniennetzplaene/";

	private const string BaseUrl = "https://www.dvb.de";

	[GeneratedRegex("dvb_lnp_\\d+_st_\\d+_jpg\\.jpg", RegexOptions.IgnoreCase)]
	private static partial Regex StandardJpg();

	[GeneratedRegex("dvb_lnp_\\d+_st_\\d+_pdf\\.pdf", RegexOptions.IgnoreCase)]
	private static partial Regex StandardPdf();

	/// <summary>The first standard plan of each kind in the page, absolute; null when the page names none.</summary>
	public static VvoNetworkMapUrls FromHtml(string html)
	{
		Uri? jpg = Find(html, StandardJpg());
		Uri? pdf = Find(html, StandardPdf());

		return new VvoNetworkMapUrls(jpg, pdf);
	}

	private static Uri? Find(string html, Regex pattern)
	{
		Match first = pattern.Match(html ?? string.Empty);

		return first.Success
			&& Uri.TryCreate(new Uri(BaseUrl), first.Value, out Uri? absolute)
				? absolute
				: null;
	}
}
