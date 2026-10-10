using DDjourneys.Core.Models;
using DDjourneys.Support;

namespace DDjourneys.Tracking.Tests;

/// <summary>Where a quoted ticket can be bought: the provider's URL first, the authority page second, nothing otherwise.</summary>
public sealed class FareLinksTests
{
	[Fact]
	public void The_tickets_own_url_wins()
	{
		JourneyFare fare =
			new()
			{
				Name = "Single",
				Url = "https://example.org/tickets"
			};

		Assert.Equal(
			"https://example.org/tickets",
			FareLinks.For(fare, "trias")?.ToString());
	}

	[Fact]
	public void The_vvo_page_stays_empty_until_the_owner_sets_it()
	{
		JourneyFare fare =
			new()
			{
				Name = "Einzelfahrt"
			};

		Assert.Null(FareLinks.For(fare, "vvo"));
	}

	[Theory]
	[InlineData("javascript:alert(1)")]
	[InlineData("file:///C:/Windows/System32/calc.exe")]
	[InlineData("intent://scan/#Intent;scheme=zxing;end")]
	public void A_sale_page_is_a_web_page(string url)
	{
		JourneyFare fare =
			new()
			{
				Name = "Single",
				Url = url
			};

		Assert.Null(FareLinks.For(fare, "trias"));
	}

	[Fact]
	public void Nothing_without_a_usable_url()
	{
		JourneyFare broken =
			new()
			{
				Name = "Single",
				Url = "not a link"
			};

		JourneyFare none =
			new()
			{
				Name = "Single"
			};

		Assert.Null(FareLinks.For(broken, "vvo"));
		Assert.Null(FareLinks.For(none, "trias"));
	}
}
