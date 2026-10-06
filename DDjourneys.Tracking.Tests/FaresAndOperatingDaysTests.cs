using System.Xml.Linq;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Trias;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Tracking.Tests;

/// <summary>Ticket choice by passenger, operating days, and their TRIAS request and response sides.</summary>
public sealed class FaresAndOperatingDaysTests
{
	private static JourneyFare Fare(string name, decimal price, FareKind kind, params PassengerCategory[] passengers) =>
		new()
		{
			Name = name,
			Price = price,
			Kind = kind,
			Passengers = passengers
		};

	[Fact]
	public void The_ticket_for_the_chosen_passenger_wins()
	{
		JourneyFare[] fares =
		[
			Fare("Day", 6.00m, FareKind.Day, PassengerCategory.Adult),
			Fare("Single child", 1.60m, FareKind.Single, PassengerCategory.Child),
			Fare("Single adult", 2.70m, FareKind.Single, PassengerCategory.Adult)
		];

		Assert.Equal("Single adult", FareChoice.Preferred(fares, PassengerCategory.Adult)?.Name);
		Assert.Equal("Single child", FareChoice.Preferred(fares, PassengerCategory.Child)?.Name);
	}

	[Fact]
	public void A_passenger_without_a_ticket_gets_the_adult_one()
	{
		JourneyFare[] fares =
		[
			Fare("Single child", 1.60m, FareKind.Single, PassengerCategory.Child),
			Fare("Single adult", 2.70m, FareKind.Single, PassengerCategory.Adult)
		];

		Assert.Equal("Single adult", FareChoice.Preferred(fares, PassengerCategory.Senior)?.Name);
	}

	[Fact]
	public void Tickets_without_a_passenger_statement_count_as_the_normal_price()
	{
		JourneyFare[] fares =
		[
			Fare("Day", 6.00m, FareKind.Day),
			Fare("Single", 2.70m, FareKind.Single)
		];

		Assert.Equal("Single", FareChoice.Preferred(fares, PassengerCategory.Adult)?.Name);
		Assert.Equal("Single", FareChoice.Preferred(fares, PassengerCategory.Child)?.Name);
	}

	[Fact]
	public void Nothing_is_chosen_without_a_price()
	{
		Assert.Null(FareChoice.Preferred([new JourneyFare { Name = "Info" }], PassengerCategory.Adult));
		Assert.Null(FareChoice.Preferred([], PassengerCategory.Adult));
	}

	[Fact]
	public void The_pattern_gives_the_weekdays_and_the_day_lookup()
	{
		var days =
			new OperatingDays
			{
				From = new DateOnly(2026, 10, 5),
				To = new DateOnly(2026, 10, 11),
				Pattern = "1111100"
			};

		Assert.Equal(
			[DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
			days.Weekdays);

		Assert.True(days.RunsOn(new DateOnly(2026, 10, 9)));
		Assert.False(days.RunsOn(new DateOnly(2026, 10, 10)));
		Assert.Null(days.RunsOn(new DateOnly(2026, 10, 12)));
	}

	[Fact]
	public void The_weekdays_are_listed_Monday_first_whatever_day_the_pattern_starts_on()
	{
		var days =
			new OperatingDays
			{
				From = new DateOnly(2026, 10, 4),
				Pattern = "1000001"
			};

		// 2026-10-04 is a Sunday: the first and the last character are Sunday and Saturday.
		Assert.Equal([DayOfWeek.Saturday, DayOfWeek.Sunday], days.Weekdays);
	}

	private static XDocument Trip(TriasDialect dialect, PassengerCategory passenger) =>
		TriasRequests.Trip(
			new JourneyQuery
			{
				From = new Location { Id = "de:14612:28", Name = "A", Kind = PlaceKind.Stop },
				To = new Location { Id = "de:14612:5", Name = "B", Kind = PlaceKind.Stop },
				DateTime = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero),
				Routing = new RoutingPreferences { Passenger = passenger }
			},
			dialect);

	private static string[] ParamNames(XDocument request) =>
		[.. request
			.Descendants()
			.First(element => element.Name.LocalName == "Params")
			.Elements()
			.Select(element => element.Name.LocalName)];

	[Fact]
	public void A_non_adult_passenger_is_sent_as_the_last_parameter()
	{
		XDocument request = Trip(TriasDialect.Latest, PassengerCategory.Child);
		string[] names = ParamNames(request);

		Assert.Equal("FaresParam", names[^1]);
		Assert.Equal("IncludeOperatingDays", names[^2]);

		Assert.Equal(
			"Child",
			request.Descendants().First(element => element.Name.LocalName == "PassengerCategory").Value);
	}

	[Fact]
	public void The_adult_is_the_default_and_not_sent()
	{
		Assert.DoesNotContain("FaresParam", ParamNames(Trip(TriasDialect.Latest, PassengerCategory.Adult)));
	}

	[Fact]
	public void Older_versions_ask_for_neither_operating_days_nor_a_passenger()
	{
		string[] v12 = ParamNames(Trip(new TriasDialect(2), PassengerCategory.Child));
		string[] v13 = ParamNames(Trip(new TriasDialect(3), PassengerCategory.Child));

		Assert.DoesNotContain("IncludeOperatingDays", v12);
		Assert.DoesNotContain("FaresParam", v12);
		Assert.Contains("IncludeOperatingDays", v13);
		Assert.DoesNotContain("FaresParam", v13);
	}

	[Fact]
	public void Operating_days_and_ticket_passengers_are_read_from_a_trip()
	{
		XDocument response =
			XDocument.Parse(
				"<Trias><TripResponse><TripResult><ResultId>r</ResultId><Trip><TripId>t</TripId>"
				+ "<TripLeg><LegId>1</LegId><TimedLeg>"
				+ "<Service/>"
				+ "<OperatingDays><From>2026-10-05</From><To>2026-10-11</To><Pattern>1111100</Pattern></OperatingDays>"
				+ "</TimedLeg></TripLeg></Trip>"
				+ "<TripFares><Ticket><TicketName>Single</TicketName><Price>1.60</Price><ValidFor>Child</ValidFor></Ticket>"
				+ "<Ticket><TicketName>Single</TicketName><Price>2.70</Price><ValidFor>Adult</ValidFor></Ticket></TripFares>"
				+ "</TripResult></TripResponse></Trias>");

		Journey journey =
			Assert.Single(
				TriasMapper.MapJourneys(
					response,
					new Location { Id = "de:14612:28", Name = "A" },
					new Location { Id = "de:14612:5", Name = "B" }));

		Assert.Equal(5, journey.Legs[0].OperatingDays?.Weekdays.Count);
		Assert.Equal([PassengerCategory.Child], journey.Fares[0].Passengers);
		Assert.Equal(2.70m, FareChoice.Preferred(journey.Fares, PassengerCategory.Adult)?.Price);
	}
}
