using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys.Tracking.Tests;

/// <summary>The DVB network map catalog read from the Liniennetzplan page: four plans, relative or absolute
/// links, a kind the page does not name is null. The file names are the ones the page listed on
/// 2026-10-12 (see VvoNetworkMap).</summary>
public sealed class VvoNetworkMapTests
{
	private const string Catalog =
		"""
		<html><body>
		<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_0501_st_2026_jpg.jpg'>Liniennetzplan (standardisiert) JPG</a>
		<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_0501_st_2026_pdf.pdf'>Liniennetzplan (standardisiert) PDF</a>
		<a href='/de-de/liniennetz/liniennetzplaene/gnl_121026_pip_jpg.jpg'>GuteNachtLinien-Baufahrplan JPG</a>
		<a href='/de-de/liniennetz/liniennetzplaene/gnl_121026_pip_pdf.pdf'>GuteNachtLinien-Baufahrplan PDF</a>
		<a href='/de-de/liniennetz/liniennetzplaene/lnp-fuer-rollstuhlnutzer_0726.pdf'>Liniennetzplan fuer mobilitaetseingeschraenkte Fahrgaeste</a>
		<a href='/de-de/liniennetz/liniennetzplaene/mobi_stadtplan_080725_web.jpg'>MOBI-Stadtplan JPG</a>
		<a href='/de-de/liniennetz/liniennetzplaene/mobi_stadtplan_080725_web-1.pdf'>MOBI-Stadtplan PDF</a>
		<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_city_121026_jpg.jpg'>Innenstadtausschnitt</a>
		<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_bau_121026_pdf.pdf'>Bau-Liniennetzplan</a>
		<a href='/de-de/liniennetz/liniennetzplaene/hs-liste-fuer-rollstuhlnutzer_0726.pdf'>Haltestellenliste</a>
		<a href='/de-de/liniennetz/liniennetzplaene/hinweise-fuer-mobilitaetseingeschraenkte-fg_01-26.pdf'>Hinweise</a>
		</body></html>
		""";

	[Fact]
	public void The_catalog_names_all_four_plans()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(Catalog);

		Assert.NotNull(catalog.Standard);
		Assert.NotNull(catalog.Night);
		Assert.NotNull(catalog.BarrierFree);
		Assert.NotNull(catalog.CityMap);

		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/dvb_lnp_0501_st_2026_jpg.jpg", catalog.Standard.Jpg!.ToString());
		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/dvb_lnp_0501_st_2026_pdf.pdf", catalog.Standard.Pdf!.ToString());
		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/gnl_121026_pip_jpg.jpg", catalog.Night.Jpg!.ToString());
		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/gnl_121026_pip_pdf.pdf", catalog.Night.Pdf!.ToString());
		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/lnp-fuer-rollstuhlnutzer_0726.pdf", catalog.BarrierFree.Pdf!.ToString());
		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/mobi_stadtplan_080725_web.jpg", catalog.CityMap.Jpg!.ToString());
		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/mobi_stadtplan_080725_web-1.pdf", catalog.CityMap.Pdf!.ToString());
	}

	[Fact]
	public void The_barrier_free_line_network_plan_is_a_pdf_only()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(Catalog);

		Assert.Null(catalog.BarrierFree!.Jpg);
		Assert.NotNull(catalog.BarrierFree.Pdf);
	}

	[Fact]
	public void The_city_map_pdf_carries_the_cms_suffix_and_still_matches()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(
			"<a href='/p/mobi_stadtplan_080725_web.jpg'>MOBI-Stadtplan</a> <a href='/p/mobi_stadtplan_080725_web-1.pdf'>MOBI-Stadtplan PDF</a>");

		Assert.Equal("https://www.dvb.de/p/mobi_stadtplan_080725_web.jpg", catalog.CityMap!.Jpg!.ToString());
		Assert.Equal("https://www.dvb.de/p/mobi_stadtplan_080725_web-1.pdf", catalog.CityMap.Pdf!.ToString());
	}

	[Fact]
	public void The_first_plan_of_a_kind_wins()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(
			"""
			<html><body>
			<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_11_st_1_jpg.jpg'>Standardplan</a>
			<a href='https://www.dvb.de/documents/2416693/2599001/dvb_lnp_11_st_1_pdf.pdf'>Standardplan PDF</a>
			<a href='/later/dvb_lnp_12_st_2_jpg.jpg'>Standardplan neu JPG</a>
			<a href='/later/dvb_lnp_12_st_2_pdf.pdf'>Standardplan neu PDF</a>
			<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_11_ih_1_jpg.jpg'>Innenstadt</a>
			<a href='/somewhere/else/plan.zip'>etwas anderes</a>
			</body></html>
			""");

		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/dvb_lnp_11_st_1_jpg.jpg", catalog.Standard!.Jpg!.ToString());
		Assert.Equal("https://www.dvb.de/documents/2416693/2599001/dvb_lnp_11_st_1_pdf.pdf", catalog.Standard.Pdf!.ToString());
	}

	[Fact]
	public void Excerpts_construction_plans_and_directories_do_not_count()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(
			"""
			<html><body>
			<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_city_121026_jpg.jpg'>Innenstadtausschnitt</a>
			<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_city_050126_standard_jpg.jpg'>Innenstadt standardisiert</a>
			<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_bau_121026_pdf.pdf'>Bau-Liniennetzplan</a>
			<a href='/de-de/liniennetz/liniennetzplaene/hs-liste-fuer-rollstuhlnutzer_0726.pdf'>Haltestellenliste</a>
			<a href='/de-de/liniennetz/liniennetzplaene/hinweise-fuer-mobilitaetseingeschraenkte-fg_01-26.pdf'>Hinweise</a>
			<a href='plan.zip'>etwas anderes</a>
			</body></html>
			""");

		Assert.Null(catalog.Standard);
		Assert.Null(catalog.Night);
		Assert.Null(catalog.BarrierFree);
		Assert.Null(catalog.CityMap);
	}

	[Fact]
	public void A_plan_the_page_does_not_name_is_null()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(
			"""
			<html><body>
			<a href='/p/dvb_lnp_0501_st_2026_jpg.jpg'>Standardplan</a>
			<a href='/p/dvb_lnp_0501_st_2026_pdf.pdf'>Standardplan PDF</a>
			<a href='/p/lnp-fuer-rollstuhlnutzer_0726.pdf'>Barrierefrei</a>
			<a href='/p/mobi_stadtplan_080725_web.jpg'>MOBI-Stadtplan</a>
			<a href='/p/mobi_stadtplan_080725_web-1.pdf'>MOBI-Stadtplan PDF</a>
			</body></html>
			""");

		Assert.Null(catalog.Night);
		Assert.NotNull(catalog.Standard);
		Assert.NotNull(catalog.BarrierFree);
		Assert.NotNull(catalog.CityMap);
	}

	[Fact]
	public void A_plan_may_be_named_by_its_bare_file_name()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(
			"<a href='gnl_121026_pip_jpg.jpg'>n</a> <a href='gnl_121026_pip_pdf.pdf'>n</a>");

		Assert.Equal("https://www.dvb.de/gnl_121026_pip_jpg.jpg", catalog.Night!.Jpg!.ToString());
		Assert.Equal("https://www.dvb.de/gnl_121026_pip_pdf.pdf", catalog.Night.Pdf!.ToString());
	}

	[Fact]
	public void Without_a_page_there_is_nothing()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(string.Empty);

		Assert.Null(catalog.Standard);
		Assert.Null(catalog.Night);
		Assert.Null(catalog.BarrierFree);
		Assert.Null(catalog.CityMap);
	}

	[Fact]
	public void The_catalog_answers_by_kind_and_compares_by_value()
	{
		VvoNetworkMapCatalog catalog = VvoNetworkMap.FromHtml(Catalog);

		Assert.Equal(catalog.Standard, catalog.Of(VvoNetworkMapKind.Standard));
		Assert.Equal(catalog.Night, catalog.Of(VvoNetworkMapKind.Night));
		Assert.Equal(catalog.BarrierFree, catalog.Of(VvoNetworkMapKind.BarrierFree));
		Assert.Equal(catalog.CityMap, catalog.Of(VvoNetworkMapKind.CityMap));

		// A record read from the page is the same as one built by hand from the same links.
		Assert.Equal(
			new VvoNetworkMapCatalog(
				null,
				new VvoNetworkMapPlan(
					new Uri("https://www.dvb.de/gnl_121026_pip_jpg.jpg"),
					new Uri("https://www.dvb.de/gnl_121026_pip_pdf.pdf")),
				null,
				null),
			VvoNetworkMap.FromHtml("<a href='/gnl_121026_pip_jpg.jpg'>n</a> <a href='/gnl_121026_pip_pdf.pdf'>n</a>"));
	}

	[Fact]
	public void The_local_names_keep_the_standard_plans_legacy_files()
	{
		Assert.Equal("networkmap", VvoNetworkMap.FileName(VvoNetworkMapKind.Standard));
		Assert.Equal("networkmap-night", VvoNetworkMap.FileName(VvoNetworkMapKind.Night));
		Assert.Equal("networkmap-barrierfree", VvoNetworkMap.FileName(VvoNetworkMapKind.BarrierFree));
		Assert.Equal("networkmap-city", VvoNetworkMap.FileName(VvoNetworkMapKind.CityMap));

		Assert.Equal("dvb", VvoNetworkMap.PlanName(VvoNetworkMapKind.Standard));
		Assert.Equal("gnl", VvoNetworkMap.PlanName(VvoNetworkMapKind.Night));
		Assert.Equal("rolli", VvoNetworkMap.PlanName(VvoNetworkMapKind.BarrierFree));
		Assert.Equal("mobi", VvoNetworkMap.PlanName(VvoNetworkMapKind.CityMap));

		// No tile name starts another, so one plan's tidy step never drops another plan's tiles.
		string[] names = [.. Enum.GetValues<VvoNetworkMapKind>().Select(VvoNetworkMap.PlanName)];

		Assert.All(names, name => Assert.Equal(1, names.Count(other => other.StartsWith(name, StringComparison.Ordinal))));
	}
}
