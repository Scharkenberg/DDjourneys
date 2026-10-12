using System.Text.RegularExpressions;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>Which of the DVB's plans a caller means.</summary>
public enum VvoNetworkMapKind
{
	/// <summary>The standard Liniennetzplan (file names like dvb_lnp_0501_st_2026).</summary>
	Standard,

	/// <summary>The GuteNachtLinien plan of the night traffic (file names like gnl_121026_pip).</summary>
	Night,

	/// <summary>The barrier-free line network plan is a PDF only; the file name is anchored at its start so the stop
	/// directory (hs-liste-fuer-rollstuhlnutzer) does not match, and it dates its edition (0726).</summary>
	BarrierFree,

	/// <summary>The MOBI city map for passengers with reduced mobility.</summary>
	CityMap
}

/// <summary>One plan of the catalog: the links the page names for it. Either may be missing; the barrier-free
/// line network plan names no JPG at all.</summary>
public sealed record VvoNetworkMapPlan(Uri? Jpg, Uri? Pdf);

/// <summary>The plans the DVB Liniennetzplan page lists, one entry per kind; a kind the page names no file of
/// is null. Asking for a null entry is a friendly error, never a crash.</summary>
public sealed record VvoNetworkMapCatalog(
		VvoNetworkMapPlan? Standard,
		VvoNetworkMapPlan? Night,
		VvoNetworkMapPlan? BarrierFree,
		VvoNetworkMapPlan? CityMap)
{
		/// <summary>The entry of one kind, null when the page names no plan of it.</summary>
		public VvoNetworkMapPlan? Of(VvoNetworkMapKind kind) =>
				kind switch
				{
						VvoNetworkMapKind.Standard => Standard,
						VvoNetworkMapKind.Night => Night,
						VvoNetworkMapKind.BarrierFree => BarrierFree,
						VvoNetworkMapKind.CityMap => CityMap,
						_ => null
				};
}

/// <summary>
/// Reads the DVB Liniennetzplan page as a catalog of plans: the standard Liniennetzplan (JPG and PDF), the
/// GuteNachtLinien plan of the night traffic, the line network plan for passengers with reduced mobility (PDF
/// only) and the MOBI city map. City-centre excerpts, construction plans, the stop directory and the notes for
/// passengers with reduced mobility are ignored on purpose. The page's structure is DVB's to change - a match
/// that fails is a friendly error, never a crash.
/// <para>
/// Verified against the page on 2026-10-12 (through a web-reader service; dvb.de is not reachable from the
/// development container): all four plans are listed on the one page, and the Nachtverkehr page links the same
/// two night plan files, so one page read serves the whole catalog. Observed file names:
/// dvb_lnp_0501_st_2026_jpg.jpg / _pdf.pdf, gnl_121026_pip_jpg.jpg / _pdf.pdf,
/// lnp-fuer-rollstuhlnutzer_0726.pdf, mobi_stadtplan_080725_web.jpg / _web-1.pdf (the CMS suffix is on the PDF).
/// </para>
/// </summary>
public static partial class VvoNetworkMap
{
	public const string PageUrl = "https://www.dvb.de/de-de/liniennetz/liniennetzplaene/";

	private const string BaseUrl = "https://www.dvb.de";

	[GeneratedRegex("href=[\"']([^\"']*dvb_lnp_\\d+_st_\\d+_jpg\\.jpg)[\"']", RegexOptions.IgnoreCase)]
	private static partial Regex StandardJpg();

	[GeneratedRegex("href=[\"']([^\"']*dvb_lnp_\\d+_st_\\d+_pdf\\.pdf)[\"']", RegexOptions.IgnoreCase)]
	private static partial Regex StandardPdf();

	// The GuteNachtLinien plan follows the standard plan's pairing. The filenames on the page have changed
	// before, so match the kind and the extension instead of one exact middle fragment.
	[GeneratedRegex("href=[\"']((?:[^\"']*/)?gnl_[^\"']+\\.jpg)[\"']", RegexOptions.IgnoreCase)]
	private static partial Regex NightJpg();

	[GeneratedRegex("href=[\"']((?:[^\"']*/)?gnl_[^\"']+\\.pdf)[\"']", RegexOptions.IgnoreCase)]
	private static partial Regex NightPdf();

	// The barrier-free line network plan is a PDF only; the file name is anchored at its start so the stop
	// directory (hs-liste-fuer-rollstuhlnutzer) does not match, and it dates its edition (0726).
	[GeneratedRegex("href=[\"']((?:[^\"']*/)?lnp-fuer-rollstuhlnutzer_\\d+\\.pdf)[\"']", RegexOptions.IgnoreCase)]
	private static partial Regex BarrierFreePdf();

	// The MOBI city map: the CMS may suffix one of the pair with -1, so the tail between _web and the
	// extension stays open (the JPG without a suffix, the PDF with one, both match).
	[GeneratedRegex("href=[\"']((?:[^\"']*/)?mobi_stadtplan_\\d+_web[^\"']*\\.jpg)[\"']", RegexOptions.IgnoreCase)]
	private static partial Regex CityJpg();

	[GeneratedRegex("href=[\"']((?:[^\"']*/)?mobi_stadtplan_\\d+_web[^\"']*\\.pdf)[\"']", RegexOptions.IgnoreCase)]
	private static partial Regex CityPdf();

	/// <summary>The plans of the page, one entry per kind; a kind the page names no file of is null.</summary>
	public static VvoNetworkMapCatalog FromHtml(string html)
	{
		return new VvoNetworkMapCatalog(
				Plan(html, StandardJpg(), StandardPdf()),
				Plan(html, NightJpg(), NightPdf()),
				Plan(html, jpg: null, pdf: BarrierFreePdf()),
				Plan(html, CityJpg(), CityPdf()));
	}

	/// <summary>The base name of a plan's cached download (the store appends .pdf or .jpg). The standard plan
	/// keeps the name it always had, so a saved plan survives an update.</summary>
	public static string FileName(VvoNetworkMapKind kind) =>
			kind switch
			{
					VvoNetworkMapKind.Standard => "networkmap",
					VvoNetworkMapKind.Night => "networkmap-night",
					VvoNetworkMapKind.BarrierFree => "networkmap-barrierfree",
					VvoNetworkMapKind.CityMap => "networkmap-city",
					_ => throw new ArgumentOutOfRangeException(nameof(kind))
			};

	/// <summary>The name a plan's tiles are kept under (the plan key's prefix in <c>Cache/plans</c>). The four
	/// names share no prefix, so each plan's tidy step drops only its own old versions; the standard plan keeps
	/// the name it always had.</summary>
	public static string PlanName(VvoNetworkMapKind kind) =>
			kind switch
			{
					VvoNetworkMapKind.Standard => "dvb",
					VvoNetworkMapKind.Night => "gnl",
					VvoNetworkMapKind.BarrierFree => "rolli",
					VvoNetworkMapKind.CityMap => "mobi",
					_ => throw new ArgumentOutOfRangeException(nameof(kind))
			};

	private static VvoNetworkMapPlan? Plan(string html, Regex? jpg, Regex pdf)
	{
		Uri? jpgLink = jpg is null ? null : Find(html, jpg);
		Uri? pdfLink = Find(html, pdf);

		return jpgLink is null && pdfLink is null
				? null
				: new VvoNetworkMapPlan(jpgLink, pdfLink);
	}

	private static Uri? Find(string html, Regex pattern)
	{
		Match first = pattern.Match(html ?? string.Empty);

		return first.Success
				&& Uri.TryCreate(new Uri(BaseUrl), first.Groups[1].Value, out Uri? absolute)
						? absolute
						: null;
	}
}
