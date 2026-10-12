using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The advice behind the ticket card of a journey: the zone count, the single-versus-day break-even,
/// the Deutschlandticket coverage and its amortization, and the BahnCard 25 estimate - numbers only,
/// the app words them in one place.
/// </summary>
public sealed class TicketAdvisorTests
{
	private static JourneyFare Fare(
		decimal price,
		FareKind kind = FareKind.Single,
		string? zones = null,
		string? description = null,
		params PassengerCategory[] passengers) =>
		new()
		{
			Name = "Fare",
			Price = price,
			Kind = kind,
			Zones = zones,
			Description = description,
			Passengers = passengers
		};

	private static JourneyLeg Leg(TransitMode mode) =>
		new()
		{
			Mode = mode,
			From = new Station { Id = "a", Name = "Origin" },
			To = new Station { Id = "z", Name = "Destination" }
		};

	private static Journey JourneyOf(params TransitMode[] modes) =>
		new()
		{
			Legs = [.. modes.Select(Leg)],
			From = new Station { Id = "a", Name = "Origin" },
			To = new Station { Id = "z", Name = "Destination" }
		};

	[Fact]
	public void The_day_ticket_wins_from_the_ride_that_makes_it_cheaper()
	{
		TicketAdvice advice =
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[
					Fare(3.10m, zones: "Dresden, Radebeul"),
					Fare(6.80m, FareKind.Day)
				],
				PassengerCategory.Adult);

		// Two singles (6,20 €) are still cheaper than the day ticket (6,80 €); three are not.
		Assert.Equal(3, advice.DayWinsFromRide);

		// A day ticket at exactly the price of two singles still needs the third ride to win.
		Assert.Equal(
			3,
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(3.40m), Fare(6.80m, FareKind.Day)],
				PassengerCategory.Adult).DayWinsFromRide);

		// A day ticket cheaper than a single wins from the first ride.
		Assert.Equal(
			1,
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(7.00m), Fare(6.80m, FareKind.Day)],
				PassengerCategory.Adult).DayWinsFromRide);

		// Without both prices there is no comparison.
		Assert.Null(
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(3.10m)],
				PassengerCategory.Adult).DayWinsFromRide);
	}

	[Fact]
	public void The_deutschlandticket_pays_for_itself_after_its_price_in_single_rides()
	{
		// 58 € at 3,10 € a ride: 18 rides (55,80 €) are not enough, 19 (58,90 €) are.
		Assert.Equal(
			19,
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(3.10m)],
				PassengerCategory.Adult).DeutschlandTicketRides);

		// An exact division breaks even at the ride that reaches the price.
		Assert.Equal(
			10,
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(5.80m)],
				PassengerCategory.Adult).DeutschlandTicketRides);

		Assert.Equal(58m, TicketAdvisor.DeutschlandTicketPrice);
	}

	[Fact]
	public void The_zones_come_from_the_fares_the_provider_quotes()
	{
		TicketAdvice advice =
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(3.10m, zones: "Dresden, Radebeul, Coswig")],
				PassengerCategory.Adult);

		Assert.Equal(3, advice.ZoneCount);
		Assert.Equal(TicketCoverage.WithinVerbund, advice.Coverage);

		// The provider's own count as words, when it names no zones.
		Assert.Equal(
			1,
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(3.10m, description: "1 Tarifzone")],
				PassengerCategory.Adult).ZoneCount);

		// A tariff level or a validity is not a zone count.
		Assert.Null(
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(3.10m, description: "Preisstufe 2 · P1D")],
				PassengerCategory.Adult).ZoneCount);
	}

	[Fact]
	public void A_journey_the_fares_say_nothing_about_is_not_claimed_as_covered()
	{
		TicketAdvice advice =
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(3.10m)],
				PassengerCategory.Adult);

		Assert.Null(advice.ZoneCount);
		Assert.Equal(TicketCoverage.Unknown, advice.Coverage);
	}

	[Fact]
	public void A_long_distance_leg_is_not_covered_by_the_deutschlandticket()
	{
		TicketAdvice advice =
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram, TransitMode.LongDistanceTrain),
				[
					Fare(3.10m, zones: "Dresden"),
					Fare(6.80m, FareKind.Day)
				],
				PassengerCategory.Adult);

		Assert.Equal(TicketCoverage.LongDistance, advice.Coverage);

		// Its break-even is not said either, and no BahnCard estimate is offered for it.
		Assert.Null(advice.DeutschlandTicketRides);
		Assert.Null(advice.BahnCard25Estimate);
	}

	[Fact]
	public void The_bahncard_estimate_is_three_quarters_of_the_passengers_single_fare()
	{
		TicketAdvice adult =
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(2.70m, zones: "Dresden")],
				PassengerCategory.Adult);

		Assert.Equal(2.03m, adult.BahnCard25Estimate);
		Assert.Equal(TicketCoverage.WithinVerbund, adult.Coverage);

		Assert.Equal(
			2.03m,
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(2.70m, zones: "Dresden")],
				PassengerCategory.Senior).BahnCard25Estimate);

		// No estimate for passengers a BahnCard 25 is not sold to, and none without a single fare.
		Assert.Null(
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(2.70m, zones: "Dresden")],
				PassengerCategory.Child).BahnCard25Estimate);

		Assert.Null(
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[Fare(6.80m, FareKind.Day, zones: "Dresden")],
				PassengerCategory.Adult).BahnCard25Estimate);

		// The passenger's own price is the one the estimate is made from.
		TicketAdvice child =
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[
					Fare(1.70m, zones: "Dresden", passengers: PassengerCategory.Child),
					Fare(2.70m, zones: "Dresden", passengers: PassengerCategory.Adult)
				],
				PassengerCategory.Child);

		Assert.Equal(1.70m, child.SinglePrice);
		Assert.Null(child.BahnCard25Estimate);
	}

	[Fact]
	public void Nothing_is_advised_without_fares()
	{
		TicketAdvice advice =
			TicketAdvisor.Advise(
				JourneyOf(TransitMode.Tram),
				[],
				PassengerCategory.Adult);

		Assert.Null(advice.ZoneCount);
		Assert.Null(advice.SinglePrice);
		Assert.Null(advice.DayWinsFromRide);
		Assert.Null(advice.DeutschlandTicketRides);
		Assert.Null(advice.BahnCard25Estimate);
		Assert.Equal(TicketCoverage.Unknown, advice.Coverage);
	}

	[Fact]
	public void The_named_ticket_list_beyond_the_two_prices_is_shown_as_the_provider_words_it()
	{
		var nothingBeyond =
			new VvoRoute
			{
				Price = "3,10",
				PriceDayTicket = "6,80",
				Tickets =
				[
					new VvoTicket { Name = "VVO Einzelfahrt", Price = "3,10" },
					new VvoTicket { Name = "VVO Tageskarte", Price = "6,80" }
				]
			};

		// The two named tickets are the two prices: the fares stay exactly the two.
		Assert.Equal(
			[FareKind.Single, FareKind.Day],
			VvoFareMapper.Map(nothingBeyond).Select(fare => fare.Kind));

		var withWeek =
			new VvoRoute
			{
				Price = "3,10",
				PriceDayTicket = "6,80",
				Tickets =
				[
					new VvoTicket { Name = "VVO Einzelfahrt", Price = "3,10" },
					new VvoTicket { Name = "VVO Wochenkarte", Price = "24,50", NumberOfFareZones = "1 Tarifzone", FareZoneNames = "TZ Dresden (10)" }
				]
			};

		IReadOnlyList<JourneyFare> fares = VvoFareMapper.Map(withWeek);

		Assert.Equal(3, fares.Count);
		Assert.Equal("VVO Wochenkarte", fares[2].Name);
		Assert.Equal(FareKind.Other, fares[2].Kind);
		Assert.Equal(24.50m, fares[2].Price);
		Assert.Equal("Dresden", fares[2].Zones);

		// A named single at another price is also shown (the provider said both); an unnamed one is not.
		var withOtherSingle =
			new VvoRoute
			{
				Price = "3,10",
				Tickets =
				[
					new VvoTicket { Name = "Einzelfahrt Bus", Price = "2,70" },
					new VvoTicket { Name = "   ", Price = "9,99" }
				]
			};

		fares = VvoFareMapper.Map(withOtherSingle);

		Assert.Equal(2, fares.Count);
		Assert.Equal("Einzelfahrt Bus", fares[1].Name);
		Assert.Equal(FareKind.Single, fares[1].Kind);
	}
}
