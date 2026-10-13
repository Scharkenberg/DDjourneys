using DDjourneys.Core.Contract;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using DDjourneys.Support.Sharing;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The share of a stop: the board link exactly as <see cref="ContractLinks.Departures"/> builds it, the
/// geo link in the interchange form of docs/EXTERNAL_CONTRACT.md §8 (which the app's own geo reader must
/// read back), and the honest cases - a stop without coordinates shares no geo link and no QR payload,
/// and the share text stays useful all the same.
/// </summary>
public sealed class StopShareTests
{
	private static Location Postplatz() =>
		new()
		{
			Id = "33000028",
			ProviderId = "vvo",
			Name = "Postplatz",
			Place = "Dresden",
			Latitude = 51.049311,
			Longitude = 13.7375
		};

	[Fact]
	public void The_board_link_is_the_contract_link_of_the_stop()
	{
		StopSharePayload share = StopShare.For(Postplatz());

		Assert.Equal("Postplatz, Dresden", share.Name);
		Assert.Equal("ddjourneys://v1/departures?at=Postplatz&at.stop=vvo%3A33000028", share.AppLink.OriginalString);

		// The link the app itself hands out must parse back through its own parser.
		ContractRequest request = ContractParser.ParseUri(share.AppLink.OriginalString).Request!;

		Assert.Equal(ContractCommand.Departures, request.Command);
		Assert.Equal("Postplatz", request.At!.Name);
		Assert.Equal("vvo:33000028", request.At.StopKey);
	}

	[Fact]
	public void The_geo_link_encodes_the_position_and_the_name()
	{
		StopSharePayload share = StopShare.For(
			new Location
			{
				Id = "de:14612:904",
				ProviderId = "vvo",
				Name = "Nürnberger Platz",
				Place = "Dresden",
				Latitude = 51.048123,
				Longitude = 13.738421
			});

		Assert.Equal("geo:51.048123,13.738421?q=N%C3%BCrnberger%20Platz", share.GeoLink);
		Assert.Equal(share.GeoLink, share.QrPayload);
		Assert.True(share.HasGeoLink);
	}

	[Fact]
	public void A_stop_without_coordinates_shares_no_geo_link_and_no_code()
	{
		Location stop =
			new()
			{
				Id = "33000028",
				ProviderId = "vvo",
				Name = "Postplatz",
				Place = "Dresden"
			};

		StopSharePayload share = StopShare.For(stop);

		Assert.Null(share.GeoLink);
		Assert.Null(share.QrPayload);
		Assert.False(share.HasGeoLink);
		Assert.StartsWith("ddjourneys://", share.AppLink.OriginalString, StringComparison.Ordinal);
	}

	[Fact]
	public void A_place_without_a_stop_key_falls_back_to_name_and_position()
	{
		StopSharePayload share = StopShare.For(
			new Location
			{
				Name = "Semperoper",
				Place = "Dresden",
				Latitude = 51.0541,
				Longitude = 13.7352
			});

		Assert.Equal("ddjourneys://v1/departures?at=Semperoper&at.lat=51.0541&at.lon=13.7352", share.AppLink.OriginalString);
		Assert.Equal("geo:51.0541,13.7352?q=Semperoper", share.GeoLink);
	}

	[Fact]
	public void The_geo_link_parses_back_through_the_apps_own_geo_reader()
	{
		string? geo = StopShare.For(
			new Location
			{
				Id = "de:14612:904",
				ProviderId = "vvo",
				Name = "Nürnberger Platz",
				Place = "Dresden",
				Latitude = 51.048123,
				Longitude = 13.738421
			}).GeoLink;

		Assert.NotNull(geo);

		ContractRequest request = ContractIntents.FromGeo(geo["geo:".Length..])!.Request!;

		Assert.Equal(ContractCommand.Go, request.Command);
		Assert.Equal("Nürnberger Platz", request.To!.Name);
		Assert.Equal(51.048123, request.To!.Latitude);
		Assert.Equal(13.738421, request.To!.Longitude);
	}

	[Fact]
	public void The_share_text_carries_the_meet_line_and_both_links()
	{
		StopSharePayload share = StopShare.For(Postplatz());

		Assert.Equal(
			"Meet me at Postplatz, Dresden.\r\n"
			+ "geo:51.049311,13.7375?q=Postplatz\r\n"
			+ "ddjourneys://v1/departures?at=Postplatz&at.stop=vvo%3A33000028\r\n"
			+ "— DDjourneys",
			StopShareText.Build(share, new EnglishUiStrings()));
	}

	[Fact]
	public void The_share_text_is_german_in_german()
	{
		StopSharePayload share = StopShare.For(Postplatz());

		Assert.Equal(
			"Treffpunkt: Postplatz, Dresden.\r\n"
			+ "geo:51.049311,13.7375?q=Postplatz\r\n"
			+ "ddjourneys://v1/departures?at=Postplatz&at.stop=vvo%3A33000028\r\n"
			+ "— DDjourneys",
			StopShareText.Build(share, new GermanUiStrings()));
	}

	[Fact]
	public void The_share_text_without_a_geo_link_still_works()
	{
		StopSharePayload share = StopShare.For(
			new Location
			{
				Id = "33000028",
				ProviderId = "vvo",
				Name = "Postplatz",
				Place = "Dresden"
			});

		Assert.Equal(
			"Meet me at Postplatz, Dresden.\r\n"
			+ "ddjourneys://v1/departures?at=Postplatz&at.stop=vvo%3A33000028\r\n"
			+ "— DDjourneys",
			StopShareText.Build(share, new EnglishUiStrings()));
	}
}
