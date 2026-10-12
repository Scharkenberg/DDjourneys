using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>Swapping one ride, and a stop-over for any start and destination, against a synthetic timetable.</summary>
public sealed class LegAlternativesAndViaTests
{
	private static readonly DateTimeOffset Day = new(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(2));

	private static readonly Station A = Stop("1", "Alpha");
	private static readonly Station B = Stop("2", "Beta");
	private static readonly Station C = Stop("3", "Gamma");

	[Fact]
	public void The_provider_s_answer_counts_only_when_the_ride_moved_to_the_wanted_side()
	{
		Journey original = Trip(Ride(A, B, "11", 10, 0, 20), Ride(B, C, "61", 10, 30, 15));

		Journey unchanged = Trip(Ride(A, B, "11", 10, 0, 20), Ride(B, C, "61", 10, 30, 15));
		Journey later = Trip(Ride(A, B, "11", 10, 10, 20), Ride(B, C, "61", 10, 40, 15));
		Journey muchLater = Trip(Ride(A, B, "11", 10, 30, 20), Ride(B, C, "61", 10, 50, 15));

		Assert.Null(LegAlternatives.Pick(original, 0, previous: false, [unchanged]));
		Assert.Same(later, LegAlternatives.Pick(original, 0, previous: false, [muchLater, later, unchanged]));
		Assert.Null(LegAlternatives.Pick(original, 0, previous: true, [later, muchLater]));
	}

	[Fact]
	public async Task A_later_ride_that_misses_the_next_connection_plans_the_rest_again()
	{
		Journey original = Trip(Ride(A, B, "11", 10, 0, 20), Ride(B, C, "61", 10, 30, 15));

		async Task<JourneyResult> Search(JourneyQuery query, CancellationToken token)
		{
			await Task.Yield();

			if (query.From.Id == "1" && query.To.Id == "2")
			{
				return JourneyResult.Success([Trip(Ride(A, B, "11", 10, 10, 20)), Trip(Ride(A, B, "11", 10, 20, 20))]);
			}

			if (query.From.Id == "2" && query.To.Id == "3")
			{
				return JourneyResult.Success([Trip(Ride(B, C, "61", 10, 40, 15))]);
			}

			return JourneyResult.Success([]);
		}

		Journey? result =
			await LegAlternatives.ComposeAsync(Search, Query(), original, 0, previous: false);

		Assert.NotNull(result);
		Assert.Equal(2, result.Legs.Count);
		Assert.Equal(Day.AddHours(10).AddMinutes(10), result.Legs[0].ScheduledDeparture);
		Assert.Equal(Day.AddHours(10).AddMinutes(40), result.Legs[1].ScheduledDeparture);
		Assert.Null(result.Context);
	}

	[Fact]
	public async Task A_later_ride_that_still_connects_keeps_the_rest_of_the_journey()
	{
		Journey original = Trip(Ride(A, B, "11", 10, 0, 10), Ride(B, C, "61", 10, 30, 15));
		JourneyLeg kept = original.Legs[1];

		Task<JourneyResult> Search(JourneyQuery query, CancellationToken token) =>
			Task.FromResult(JourneyResult.Success([Trip(Ride(A, B, "11", 10, 10, 10))]));

		Journey? result =
			await LegAlternatives.ComposeAsync(Search, Query(), original, 0, previous: false);

		Assert.NotNull(result);
		Assert.Same(kept, result.Legs[1]);
		Assert.Equal(Day.AddHours(10).AddMinutes(10), result.Legs[0].ScheduledDeparture);
	}

	[Fact]
	public async Task Without_another_ride_there_is_no_alternative()
	{
		Journey original = Trip(Ride(A, B, "11", 10, 0, 20));

		Task<JourneyResult> Search(JourneyQuery query, CancellationToken token) =>
			Task.FromResult(JourneyResult.Success([Trip(Ride(A, B, "11", 10, 0, 20))]));

		Assert.Null(await LegAlternatives.ComposeAsync(Search, Query(), original, 0, previous: false));
	}

	[Fact]
	public void A_journey_passes_a_stop_over_when_one_of_its_stops_is_that_place()
	{
		Journey journey = Trip(Ride(A, B, "11", 10, 0, 20), Ride(B, C, "61", 10, 30, 15));

		Assert.True(ViaRouting.Passes(journey, ToLocation(B)));
		Assert.False(ViaRouting.Passes(Trip(Ride(A, C, "5", 10, 0, 30)), ToLocation(B)));
	}

	[Fact]
	public async Task A_stop_over_is_built_from_two_searches()
	{
		Task<JourneyResult> Search(JourneyQuery query, CancellationToken token) =>
			Task.FromResult(
				query switch
				{
					{ From.Id: "1", To.Id: "2" } => JourneyResult.Success([Trip(Ride(A, B, "11", 10, 0, 20))]),
					{ From.Id: "2", To.Id: "3" } => JourneyResult.Success([Trip(Ride(B, C, "61", 10, 30, 15))]),
					_ => JourneyResult.Success([])
				});

		JourneyQuery query = Query(via: ToLocation(B));

		IReadOnlyList<Journey> built = await ViaRouting.ComposeAsync(Search, query);

		Journey only = Assert.Single(built);
		Assert.Equal(2, only.Legs.Count);
		Assert.True(ViaRouting.Passes(only, ToLocation(B)));
		Assert.Equal(C.Id, only.To.Id);
	}

	[Fact]
	public void Runs_of_a_journey_keep_their_transfers_when_cut_and_joined()
	{
		JourneyLeg first = Ride(A, B, "11", 10, 0, 10);
		JourneyLeg second = Ride(B, C, "61", 10, 30, 15);

		var journey = new Journey
		{
			Legs = [first, second],
			From = A,
			To = C,
			Transfers =
			[
				new JourneyTransfer { Location = A, PreviousLegIndex = null, NextLegIndex = 0, Duration = TimeSpan.FromMinutes(3) },
				new JourneyTransfer { Location = B, PreviousLegIndex = 0, NextLegIndex = 1, Duration = TimeSpan.FromMinutes(2) },
				new JourneyTransfer { Location = C, PreviousLegIndex = 1, NextLegIndex = null, Duration = TimeSpan.FromMinutes(4) }
			]
		};

		Journey joined =
			JourneySplice.Assemble(
				journey,
				[JourneySplice.Slice(journey, 0, 1), JourneySplice.Slice(journey, 1, 2)]);

		Assert.Equal(3, joined.Transfers.Count);
		Assert.Contains(joined.Transfers, item => item.PreviousLegIndex is null && item.NextLegIndex == 0);
		Assert.Contains(joined.Transfers, item => item.PreviousLegIndex == 0 && item.NextLegIndex == 1);
		Assert.Contains(joined.Transfers, item => item.PreviousLegIndex == 1 && item.NextLegIndex is null);
	}

	[Fact]
	public void A_transfer_at_the_stop_over_measures_the_gap_between_the_legs()
	{
		Journey journey = Trip(Ride(A, B, "11", 10, 0, 20), Ride(B, C, "61", 10, 26, 15));

		Assert.Equal(TimeSpan.FromMinutes(6), ViaRouting.DwellOf(journey, ToLocation(B)));
	}

	[Fact]
	public void A_stop_of_a_ride_measures_the_stop_s_own_dwell()
	{

		Journey journey =
			new()
			{
				Legs = [new JourneyLeg
				{
					Mode = TransitMode.Tram,
					From = A,
					To = C,
					Line = new TransitLine { Name = "5", Mode = TransitMode.Tram },
					ScheduledDeparture = Day.AddHours(10),
					ScheduledArrival = Day.AddHours(10).AddMinutes(30),
					Stops =
					[
						new StopTime { Station = A, ScheduledDeparture = Day.AddHours(10) },
						new StopTime { Station = B, ScheduledArrival = Day.AddHours(10).AddMinutes(12), ScheduledDeparture = Day.AddHours(10).AddMinutes(16) },
						new StopTime { Station = C, ScheduledArrival = Day.AddHours(10).AddMinutes(30) }
					]
				}],
				From = A,
				To = C
			};

		Assert.Equal(TimeSpan.FromMinutes(4), ViaRouting.DwellOf(journey, ToLocation(B)));
	}

	[Fact]
	public void A_pass_through_that_does_not_stop_has_no_dwell()
	{
		Journey journey =
			new()
			{
				Legs = [new JourneyLeg
				{
					Mode = TransitMode.Tram,
					From = A,
					To = C,
					Line = new TransitLine { Name = "5", Mode = TransitMode.Tram },
					ScheduledDeparture = Day.AddHours(10),
					ScheduledArrival = Day.AddHours(10).AddMinutes(30),
					Stops = [new StopTime { Station = B }]
				}],
				From = A,
				To = C
			};

		Assert.Equal(TimeSpan.Zero, ViaRouting.DwellOf(journey, ToLocation(B)));
	}

	[Fact]
	public void The_dwell_filter_starts_at_two_minutes_and_keeps_the_unmeasurable()
	{
		Journey staying = Trip(Ride(A, B, "11", 10, 0, 20), Ride(B, C, "61", 10, 30, 15));
		Journey passingThrough = Trip(Ride(A, B, "11", 10, 0, 20), Ride(B, C, "61", 10, 21, 15));
		Journey startingThere = Trip(Ride(B, C, "61", 10, 0, 15));

		Assert.True(ViaRouting.KeepsDwell(staying, ToLocation(B), 1));
		Assert.True(ViaRouting.KeepsDwell(staying, ToLocation(B), 0));
		Assert.True(ViaRouting.KeepsDwell(passingThrough, ToLocation(B), 1));
		Assert.False(ViaRouting.KeepsDwell(passingThrough, ToLocation(B), 3));
		Assert.True(ViaRouting.KeepsDwell(staying, ToLocation(B), 10));
		Assert.True(ViaRouting.KeepsDwell(startingThere, ToLocation(B), 30));
	}

	[Fact]
	public async Task The_two_search_fallback_fits_its_join_to_the_asked_stay()
	{
		DateTimeOffset askedForTail = default;

		Task<JourneyResult> Search(JourneyQuery query, CancellationToken token)
		{
			if (query.From.Id == "2" && query.To.Id == "3")
			{
				askedForTail = query.DateTime;
			}

			return Task.FromResult(
				query switch
				{
					{ From.Id: "1", To.Id: "2" } => JourneyResult.Success([Trip(Ride(A, B, "11", 10, 0, 20))]),
					{ From.Id: "2", To.Id: "3" } => JourneyResult.Success([Trip(Ride(B, C, "61", 10, 30, 15))]),
					_ => JourneyResult.Success([])
				});
		}

		JourneyQuery query =
			new()
			{
				From = ToLocation(A),
				To = ToLocation(C),
				Via = ToLocation(B),
				DateTime = Day.AddHours(10),
				MaxResults = 3,
				Routing = new RoutingPreferences { ViaMinutes = 5 }
			};

		IReadOnlyList<Journey> built = await ViaRouting.ComposeAsync(Search, query);

		Assert.NotEmpty(built);
		Assert.Equal(Day.AddHours(10).AddMinutes(20).AddMinutes(5), askedForTail);
	}

	private static JourneyQuery Query(Location? via = null) =>
		new()
		{
			From = ToLocation(A),
			To = ToLocation(C),
			Via = via,
			DateTime = Day.AddHours(10),
			MaxResults = 3
		};

	private static Location ToLocation(Station station) =>
		new()
		{
			Id = station.Id,
			Name = station.Name,
			Kind = PlaceKind.Stop
		};

	private static Station Stop(string id, string name) =>
		new()
		{
			Id = id,
			Name = name
		};

	/// <summary>A ride of the line that leaves at hour:minute and takes <paramref name="minutes"/>.</summary>
	private static JourneyLeg Ride(Station from, Station to, string line, int hour, int minute, int minutes)
	{
		DateTimeOffset start = Day.AddHours(hour).AddMinutes(minute);

		return new JourneyLeg
		{
			Mode = TransitMode.Tram,
			From = from,
			To = to,
			Line = new TransitLine { Name = line, Mode = TransitMode.Tram },
			ScheduledDeparture = start,
			ScheduledArrival = start.AddMinutes(minutes)
		};
	}

	private static Journey Trip(params JourneyLeg[] legs) =>
		new()
		{
			Legs = legs,
			From = legs[0].From,
			To = legs[^1].To,
			Id = "route",
			Context = "session"
		};
}
