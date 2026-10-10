using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys.Tracking.Tests;

/// <summary>The DVB network map links read from the page: standard plan only, relative or absolute.</summary>
public sealed class VvoNetworkMapTests
{
	private const string Page =
		"""
		<html><body>
		<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_11_st_1_jpg.jpg'>Standardplan</a>
		<a href='https://www.dvb.de/documents/2416693/2599001/dvb_lnp_11_st_1_pdf.pdf'>Standardplan PDF</a>
		<a href='/de-de/liniennetz/liniennetzplaene/dvb_lnp_11_ih_1_jpg.jpg'>Innenstadt</a>
		<a href='/somewhere/else/plan.zip'>etwas anderes</a>
		</body></html>
		""";

	[Fact]
	public void The_first_standard_plan_of_each_kind_wins()
	{
		VvoNetworkMapUrls urls = VvoNetworkMap.FromHtml(Page);

		Assert.NotNull(urls.Jpg);
		Assert.NotNull(urls.Pdf);
		Assert.Equal("https://www.dvb.de/de-de/liniennetz/liniennetzplaene/dvb_lnp_11_st_1_jpg.jpg", urls.Jpg.ToString());
		Assert.Equal("https://www.dvb.de/documents/2416693/2599001/dvb_lnp_11_st_1_pdf.pdf", urls.Pdf.ToString());
	}

	[Fact]
	public void Excerpts_and_other_files_do_not_count()
	{
		VvoNetworkMapUrls urls = VvoNetworkMap.FromHtml(
			"<a href='dvb_lnp_11_ih_1_jpg.jpg'>city centre</a> <a href='plan.zip'>zip</a>");

		Assert.Null(urls.Jpg);
		Assert.Null(urls.Pdf);
	}

	[Fact]
	public void Without_a_page_there_is_nothing()
	{
		VvoNetworkMapUrls urls = VvoNetworkMap.FromHtml(string.Empty);

		Assert.Null(urls.Jpg);
		Assert.Null(urls.Pdf);
	}
}
