using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>Result count and earlier/later pages, against a synthetic timetable.</summary>
public sealed class JourneyWindowTests
{
	private static readonly DateTimeOffset Day = new(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(2));

	// ----- Number of journeys per search -----

	[Fact]
	public async Task A_short_answer_is_topped_up_to_the_requested_number()
	{
		var timetable = new Timetable();

		IReadOnlyList<Journey> journeys = await Fill(timetable, At(8, 0), JourneySearchMode.Departure, 7);

		Assert.Equal(Minutes(8, 0, 7), Starts(journeys));
	}

	[Fact]
	public async Task A_long_answer_is_cut_to_the_requested_number()
	{
		var timetable = new Timetable(answerSize: 6);

		IReadOnlyList<Journey> journeys = await Fill(timetable, At(8, 0), JourneySearchMode.Departure, 3);

		Assert.Equal(Minutes(8, 0, 3), Starts(journeys));
		Assert.Equal(1, timetable.Calls);
	}

	[Fact]
	public async Task Arrive_by_keeps_the_latest_arrivals_before_the_time()
	{
		var timetable = new Timetable();

		IReadOnlyList<Journey> journeys = await Fill(timetable, At(9, 0), JourneySearchMode.Arrival, 6);

		// Every 10 minutes, 20 minutes long: the last arrival up to 09:00 is the 08:40 departure.
		Assert.Equal(Minutes(7, 50, 6), Starts(journeys));
	}

	[Fact]
	public async Task The_service_enforces_the_number_for_every_provider()
	{
		var timetable = new Timetable();
		var service = new JourneyService([timetable]);

		JourneyResult result = await service.SearchAsync(Query(At(8, 0), JourneySearchMode.Departure, 9));

		Assert.Equal(9, result.Journeys.Count);
	}

	// ----- Earlier / later -----

	[Fact]
	public async Task Later_continues_right_after_the_last_journey_without_repeats()
	{
		var timetable = new Timetable();
		IReadOnlyList<Journey> shown = await Fill(timetable, At(8, 0), JourneySearchMode.Departure, 5);

		PageResult page = await Page(timetable, shown, previous: false, 5);

		Assert.Equal(Minutes(8, 50, 5), Starts(page.Journeys));
	}

	[Fact]
	public async Task Earlier_ends_right_before_the_first_journey_without_repeats()
	{
		var timetable = new Timetable();
		IReadOnlyList<Journey> shown = await Fill(timetable, At(8, 0), JourneySearchMode.Departure, 5);

		PageResult page = await Page(timetable, shown, previous: true, 5);

		Assert.Equal(Minutes(7, 10, 5), Starts(page.Journeys));
	}

	[Fact]
	public async Task Pages_can_be_followed_repeatedly_in_both_directions()
	{
		var timetable = new Timetable();
		IReadOnlyList<Journey> shown = await Fill(timetable, At(12, 0), JourneySearchMode.Departure, 4);

		for (int i = 0; i < 3; i++)
		{
			shown = (await Page(timetable, shown, previous: false, 4)).Journeys;
		}

		Assert.Equal(Minutes(14, 0, 4), Starts(shown));

		for (int i = 0; i < 3; i++)
		{
			shown = (await Page(timetable, shown, previous: true, 4)).Journeys;
		}

		Assert.Equal(Minutes(12, 0, 4), Starts(shown));
	}

	[Fact]
	public async Task Later_in_an_arrive_by_list_follows_the_arrivals()
	{
		var timetable = new Timetable();
		IReadOnlyList<Journey> shown = await Fill(timetable, At(9, 0), JourneySearchMode.Arrival, 3);

		PageResult page = await Page(timetable, shown, previous: false, 3, JourneySearchMode.Arrival);

		Assert.Equal(Minutes(8, 50, 3), Starts(page.Journeys));
	}

	[Fact]
	public async Task The_end_of_service_gives_a_short_page_and_stops()
	{
		var timetable = new Timetable();
		IReadOnlyList<Journey> shown = await Fill(timetable, At(23, 0), JourneySearchMode.Departure, 3);

		PageResult page = await Page(timetable, shown, previous: false, 5);

		Assert.Equal(Minutes(23, 30, 3), Starts(page.Journeys));
		Assert.True(timetable.Calls <= 1 + 1 + JourneyWindow.MaxRounds);
	}

	[Fact]
	public async Task A_provider_that_repeats_itself_cannot_cause_a_loop()
	{
		var stubborn = new Timetable(ignoreTime: true);
		IReadOnlyList<Journey> shown = await Fill(stubborn, At(8, 0), JourneySearchMode.Departure, 4);
		int before = stubborn.Calls;

		PageResult page = await Page(stubborn, shown, previous: false, 4);

		Assert.Empty(page.Journeys);
		Assert.Null(page.Failure);
		Assert.True(stubborn.Calls - before <= JourneyWindow.MaxRounds);
	}

	[Fact]
	public async Task A_failing_provider_is_reported_when_nothing_was_found()
	{
		var timetable = new Timetable();
		IReadOnlyList<Journey> shown = await Fill(timetable, At(8, 0), JourneySearchMode.Departure, 3);

		timetable.Fail = true;

		PageResult page = await Page(timetable, shown, previous: false, 3);

		Assert.Empty(page.Journeys);
		Assert.Equal(JourneyOutcome.Failed, page.Failure?.Outcome);
	}

	[Fact]
	public async Task The_service_pages_without_a_provider_session()
	{
		var timetable = new Timetable();
		var service = new JourneyService([timetable]);
		JourneyQuery query = Query(At(8, 0), JourneySearchMode.Departure, 4);

		JourneyResult first = await service.SearchAsync(query);
		JourneyResult later = await service.PageAsync(query, first.Journeys, previous: false, 4);

		Assert.Equal(Minutes(8, 40, 4), Starts(later.Journeys));
		Assert.All(later.Journeys, journey => Assert.Null(journey.Context));
	}

	// ----- Helpers -----

	private static Task<IReadOnlyList<Journey>> Fill(
		Timetable timetable,
		DateTimeOffset time,
		JourneySearchMode mode,
		int wanted) =>
		Search(timetable, Query(time, mode, wanted), wanted);

	private static async Task<IReadOnlyList<Journey>> Search(Timetable timetable, JourneyQuery query, int wanted)
	{
		JourneyResult first = await timetable.SearchAsync(query);

		return await JourneyWindow.FillAsync(timetable.SearchAsync, query, first.Journeys, wanted);
	}

	private static Task<PageResult> Page(
		Timetable timetable,
		IReadOnlyList<Journey> shown,
		bool previous,
		int wanted,
		JourneySearchMode mode = JourneySearchMode.Departure) =>
		JourneyWindow.PageAsync(
			timetable.SearchAsync,
			Query(At(8, 0), mode, wanted),
			shown,
			previous,
			wanted);

	private static JourneyQuery Query(DateTimeOffset time, JourneySearchMode mode, int wanted) =>
		new()
		{
			From = new Location { Id = "a", Name = "A" },
			To = new Location { Id = "b", Name = "B" },
			DateTime = time,
			SearchMode = mode,
			MaxResults = wanted
		};

	private static DateTimeOffset At(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);

	/// <summary>Departure minutes of the day, every 10 minutes.</summary>
	private static int[] Minutes(int hour, int minute, int count) =>
		[.. Enumerable.Range(0, count).Select(i => hour * 60 + minute + i * 10)];

	private static int[] Starts(IEnumerable<Journey> journeys) =>
		[.. journeys.Select(journey => (int)(JourneyWindow.PlannedStart(journey)!.Value - Day).TotalMinutes)];

	/// <summary>
	/// Line 1 every 10 minutes from 05:00 to 23:50, 20 minutes long. A search answers four journeys
	/// (departing from the time on, or arriving up to it), like a typical provider.
	/// </summary>
	private sealed class Timetable(int answerSize = 4, bool ignoreTime = false) : IJourneyProvider
	{
		private static readonly TimeSpan Ride = TimeSpan.FromMinutes(20);

		public int Calls { get; private set; }

		public bool Fail { get; set; }

		public Task<JourneyResult> SearchAsync(JourneyQuery query, CancellationToken cancellationToken = default)
		{
			Calls++;

			if (Fail)
			{
				return Task.FromResult(JourneyResult.Failure("down"));
			}

			IEnumerable<DateTimeOffset> all =
				Enumerable.Range(0, 114).Select(i => Day.AddHours(5).AddMinutes(i * 10));

			DateTimeOffset time = ignoreTime ? Day.AddHours(8) : query.DateTime;

			DateTimeOffset[] starts =
				query.SearchMode == JourneySearchMode.Arrival && !ignoreTime
					? [.. all.Where(start => start + Ride <= time).TakeLast(answerSize)]
					: [.. all.Where(start => start >= time).Take(answerSize)];

			return Task.FromResult(JourneyResult.Success([.. starts.Select(Build)]));
		}

		private static Journey Build(DateTimeOffset start)
		{
			var from = new Station { Id = "a", Name = "A" };
			var to = new Station { Id = "b", Name = "B" };

			return new Journey
			{
				From = from,
				To = to,
				Legs =
				[
					new JourneyLeg
					{
						Mode = TransitMode.Tram,
						From = from,
						To = to,
						Line = new TransitLine { Name = "1", Mode = TransitMode.Tram },
						ScheduledDeparture = start,
						ScheduledArrival = start + Ride
					}
				]
			};
		}
	}
}
