using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Support;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// On-demand legs (Anruf-Linien-Bus, the DVB brand alita): how the VVO data marks them, how their
/// booking contact travels to the leg, and where the booking link goes - only what the data carries,
/// the authority's page otherwise, never an invented number.
/// </summary>
public sealed class OnDemandTests
{
	[Theory]
	[InlineData("Alita", TransitMode.OnDemand)]
	[InlineData("alita", TransitMode.OnDemand)]
	[InlineData("HailedSharedTaxi", TransitMode.OnDemand)]
	[InlineData("Tram", TransitMode.Tram)]
	[InlineData("Train", TransitMode.RegionalTrain)]
	[InlineData("Footpath", TransitMode.Walk)]
	public void On_demand_runs_are_marked_by_their_mot_type(
		string type,
		TransitMode expected)
	{
		Assert.Equal(
			expected,
			VvoModeMapper.MapMode(
				new VvoMot { Type = type, Name = "alita 76" }));
	}

	[Fact]
	public void A_taxi_said_by_the_line_name_alone_stays_a_taxi()
	{
		Assert.Equal(
			TransitMode.Taxi,
			VvoModeMapper.MapMode(
				new VvoMot { Name = "Taxi" }));

		// A type of its own wins over the name: a PlusBus named "Taxi" is a bus.
		Assert.Equal(
			TransitMode.Bus,
			VvoModeMapper.MapMode(
				new VvoMot { Type = "PlusBus", Name = "Taxi" }));
	}

	[Fact]
	public void On_demand_legs_are_individual_transport_like_taxis()
	{
		Assert.False(TransitMode.OnDemand.IsRide());
		Assert.True(TransitMode.Tram.IsRide());
	}

	[Fact]
	public void The_booking_contact_of_the_data_travels_to_the_leg()
	{
		DateTimeOffset start = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

		JourneyLeg leg =
			VvoLegMapper.MapLeg(
				new VvoPartialRoute
				{
					PartialRouteId = 3,
					Duration = 12,

					Mot =
						new VvoMot
						{
							Type = "Alita",
							Name = "alita 76",
							Direction = "Wilder Mann"
						},

					BookingLink = "0351 8526555",

					RegularStops =
					[
						new VvoStop
						{
							DataId = "a",
							Name = "Origin",
							Place = "Dresden",
							Type = "Stop",
							DepartureTime = start,
							ArrivalTime = start,
							Latitude = 4500000,
							Longitude = 5650000
						},

						new VvoStop
						{
							DataId = "z",
							Name = "Destination",
							Place = "Dresden",
							Type = "Stop",
							ArrivalTime = start.AddMinutes(12),
							DepartureTime = start.AddMinutes(12),
							Latitude = 4500100,
							Longitude = 5650100
						}
					]
				},
				previousDestination: null,
				nextOrigin: null,
				routeCancelled: false,
				path: []);

		Assert.Equal(TransitMode.OnDemand, leg.Mode);
		Assert.Equal("alita 76", leg.Line?.Name);
		Assert.Equal("0351 8526555", leg.BookingContact);
		Assert.True(OnDemandBooking.IsOnDemand(leg));

		Assert.False(
			OnDemandBooking.IsOnDemand(
				new JourneyLeg
				{
					Mode = TransitMode.Tram,
					From = new Station { Id = "a", Name = "A" },
					To = new Station { Id = "z", Name = "Z" }
				}));
	}

	[Fact]
	public void The_booking_link_is_the_data_one_the_authority_page_otherwise()
	{
		// A web page is a web page, like a sale page: no scripts, files or app intents.
		Assert.Equal(
			"https://alita.example/booking",
			OnDemandBooking.LinkOf("https://alita.example/booking").ToString());

		// A phone number becomes a tel: link, written the way the data writes it.
		Assert.Equal(
			"tel:03518526555",
			OnDemandBooking.LinkOf("0351 8526555").ToString());
		Assert.Equal(
			"tel:+493518526555",
			OnDemandBooking.LinkOf("+49 351 8526555").ToString());
		Assert.Equal(
			"tel:+493518526555",
			OnDemandBooking.LinkOf("tel:+493518526555").ToString());

		// Everything else - nothing, a text that is no number, an address - is the authority's page.
		Assert.Equal(
			"https://www.dvb.de/de-de/fahrplan/nachtverkehr/anruflinientaxi",
			OnDemandBooking.LinkOf(null).ToString());
		Assert.Equal(
			"https://www.dvb.de/de-de/fahrplan/nachtverkehr/anruflinientaxi",
			OnDemandBooking.LinkOf("service@vvo-online.de").ToString());
		Assert.Equal(
			"https://www.dvb.de/de-de/fahrplan/nachtverkehr/anruflinientaxi",
			OnDemandBooking.LinkOf("javascript:alert(1)").ToString());
		Assert.Equal(
			"https://www.dvb.de/de-de/fahrplan/nachtverkehr/anruflinientaxi",
			OnDemandBooking.LinkOf(" 12 ").ToString());
	}

	[Fact]
	public void The_label_names_the_providers_own_number_when_it_carries_one()
	{
		Assert.Equal("0351 8526555", OnDemandBooking.LabelOf("0351 8526555"));
		Assert.Equal("8526555", OnDemandBooking.LabelOf("tel:8526555"));

		// No number of its own: the caller shows its own words.
		Assert.Null(OnDemandBooking.LabelOf(null));
		Assert.Null(OnDemandBooking.LabelOf("https://alita.example/booking"));
		Assert.Null(OnDemandBooking.LabelOf("service@vvo-online.de"));
	}
}
