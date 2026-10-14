using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The next departures of one stop or of several around a position (B1): the one path the "next
/// departures around" widget and the departures page's nearby rows share — the room a line filter needs,
/// the per-stop cut, the few-at-a-time pass in which one failed stop answers empty, and what a nearby
/// row previews.
/// </summary>
public class NearbyBoardsTests
{
	private static readonly DateTimeOffset At = new(2026, 10, 5, 7, 20, 0, TimeSpan.FromHours(2));

	private static Location Stop(string name) =>
		new()
		{
			Id = $"de:14612:{name}",
			ProviderId = "test-provider",
			Name = name,
			Latitude = 51.05,
			Longitude = 13.74,
			Kind = PlaceKind.Stop
		};

	private static Departure Dep(
		string line,
		int minutes,
		bool cancelled = false) =>
		new()
		{
			Id = $"{line}-{minutes}",
			StopId = "33000028",
			Line = new TransitLine
			{
				Name = line,
				Mode = TransitMode.Tram,
				Destination = "Bühlau"
			},
			Scheduled = At.AddMinutes(minutes),
			State = cancelled ? DepartureState.Cancelled : DepartureState.InTime
		};

	[Fact]
	public void Without_a_line_filter_only_the_first_rows_are_kept()
	{
		IReadOnlyList<Departure> kept = NearbyBoards.Keep(
			[Dep("11", 0), Dep("66", 5), Dep("8", 9)],
			keep: 2,
			lines: null);

		Assert.Equal(["11-0", "66-5"], [.. kept.Select(departure => departure.Id)]);
	}

	[Fact]
	public void A_line_filter_keeps_only_its_lines_still_cut_to_the_rows_kept()
	{
		IReadOnlyList<Departure> kept = NearbyBoards.Keep(
			[Dep("11", 0), Dep("66", 5), Dep("11", 9), Dep("66", 12)],
			keep: 2,
			lines: ["66"]);

		Assert.Equal(["66-5", "66-12"], [.. kept.Select(departure => departure.Id)]);
	}

	[Fact]
	public async Task A_line_filter_is_asked_for_three_times_as_many_rows()
	{
		FakeBoards provider = new((_, _) => Task.FromResult<IReadOnlyList<Departure>>([Dep("11", 0), Dep("66", 5)]));

		await NearbyBoards.NextOfAsync(
			new DepartureService([provider]),
			Stop("Postplatz"),
			arrival: false,
			keep: 2,
			lines: ["11"],
			modes: ModeFilter.All,
			timeoutSeconds: 15, TestContext.Current.CancellationToken);

		Assert.Equal(6, provider.Queries[0].Limit);
	}

	[Fact]
	public async Task Without_a_line_filter_the_rows_kept_are_asked_for_directly()
	{
		FakeBoards provider = new((_, _) => Task.FromResult<IReadOnlyList<Departure>>([Dep("11", 0)]));

		await NearbyBoards.NextOfAsync(
			new DepartureService([provider]),
			Stop("Postplatz"),
			arrival: true,
			keep: 4,
			lines: null,
			modes: ModeFilter.Tram,
			timeoutSeconds: 15, TestContext.Current.CancellationToken);

		Assert.Equal(4, provider.Queries[0].Limit);
		Assert.True(provider.Queries[0].IsArrival);
		Assert.Equal(ModeFilter.Tram, provider.Queries[0].Modes);
	}

	[Fact]
	public async Task One_board_per_stop_in_the_order_the_stops_were_given()
	{
		FakeBoards provider = new(
			(stop, _) => Task.FromResult<IReadOnlyList<Departure>>([Dep(stop.Name, 0)]));

		IReadOnlyList<Departure>[] boards = await NearbyBoards.LoadAsync(
			new DepartureService([provider]),
			[Stop("Postplatz"), Stop("Altmarkt"), Stop("Helgoland")],
			arrival: false,
			keep: 1,
			lines: null,
			modes: ModeFilter.All,
			timeoutSeconds: 15, TestContext.Current.CancellationToken);

		Assert.Equal(3, boards.Length);
		Assert.Equal("Postplatz-0", boards[0][0].Id);
		Assert.Equal("Altmarkt-0", boards[1][0].Id);
		Assert.Equal("Helgoland-0", boards[2][0].Id);
	}

	[Fact]
	public async Task A_stop_that_fails_answers_empty_the_rest_still_loads()
	{
		FakeBoards provider = new(
			(stop, _) => stop.Name == "Altmarkt"
				? throw new InvalidOperationException("no answer")
				: Task.FromResult<IReadOnlyList<Departure>>([Dep(stop.Name, 0)]));

		IReadOnlyList<Departure>[] boards = await NearbyBoards.LoadAsync(
			new DepartureService([provider]),
			[Stop("Postplatz"), Stop("Altmarkt"), Stop("Helgoland")],
			arrival: false,
			keep: 1,
			lines: null,
			modes: ModeFilter.All,
			timeoutSeconds: 15, TestContext.Current.CancellationToken);

		Assert.Equal(3, boards.Length);
		Assert.Single(boards[0]);
		Assert.Empty(boards[1]);
		Assert.Single(boards[2]);
	}

	[Fact]
	public async Task At_most_three_stops_are_asked_at_the_same_time()
	{
		int busy = 0;
		int busiest = 0;

		FakeBoards provider = new(
			async (_, _) =>
			{
				int now = Interlocked.Increment(ref busy);
				Interlocked.Exchange(ref busiest, Math.Max(busiest, now));

				try
				{
					await Task.Delay(60);
				}
				finally
				{
					Interlocked.Decrement(ref busy);
				}

				return [Dep("11", 0)];
			});

		await NearbyBoards.LoadAsync(
			new DepartureService([provider]),
			[Stop("a"), Stop("b"), Stop("c"), Stop("d"), Stop("e")],
			arrival: false,
			keep: 1,
			lines: null,
			modes: ModeFilter.All,
			timeoutSeconds: 15, TestContext.Current.CancellationToken);

		Assert.InRange(busiest, 2, NearbyBoards.AtATime);
	}

	[Fact]
	public void The_preview_skips_cancelled_and_gone_rows_and_keeps_the_board_order()
	{
		DateTimeOffset now = At;

		IReadOnlyList<Departure> previews = NearbyBoards.Previews(
			[
				Dep("11", -5),                    // long gone
				Dep("66", 0, cancelled: true),    // does not leave
				Dep("11", 0),
				Dep("8", 3),
				Dep("61", 9)
			],
			count: 3,
			now: now);

		Assert.Equal(["11-0", "8-3", "61-9"], [.. previews.Select(departure => departure.Id)]);
	}

	[Fact]
	public void A_departure_that_just_left_is_still_a_preview()
	{
		IReadOnlyList<Departure> previews = NearbyBoards.Previews(
			[Dep("11", 0)],
			count: 3,
			now: At.AddSeconds(30));

		Assert.Single(previews);
	}

	[Fact]
	public void Zero_previews_answer_nothing()
	{
		Assert.Empty(NearbyBoards.Previews([Dep("11", 0)], count: 0, now: At));
	}

	[Fact]
	public async Task No_stops_answer_no_boards()
	{
		FakeBoards provider = new((_, _) => Task.FromResult<IReadOnlyList<Departure>>([Dep("11", 0)]));

		IReadOnlyList<Departure>[] boards = await NearbyBoards.LoadAsync(
			new DepartureService([provider]),
			[],
			arrival: false,
			keep: 1,
			lines: null,
			modes: ModeFilter.All,
			timeoutSeconds: 15, TestContext.Current.CancellationToken);

		Assert.Empty(boards);
		Assert.Empty(provider.Queries);
	}

	/// <summary>A departure provider whose answer the test fills in (a stand-in; not a real provider id).</summary>
	private sealed class FakeBoards(
		Func<Location, DepartureQuery, Task<IReadOnlyList<Departure>>> answer) : IDepartureProvider
	{
		public List<DepartureQuery> Queries { get; } = [];

		public async Task<DepartureBoard> GetDeparturesAsync(
			DepartureQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);

			return new DepartureBoard
			{
				StopName = query.Stop.Name,
				Departures = await answer(query.Stop, query)
			};
		}

		public Task<IReadOnlyList<RunStop>> GetRunAsync(
			Departure departure,
			int timeoutSeconds = 15,
			CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("No run is asked for in these tests.");
	}
}
