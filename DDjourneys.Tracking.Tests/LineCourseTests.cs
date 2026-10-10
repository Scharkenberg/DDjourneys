using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The whole-line course of Tier 4: the de-duplication of the window's stops, the direction letter, the
/// course from a run detail, and the both-directions pick from a stop's board (a fake departure provider
/// stands in for the real one).
/// </summary>
public class LineCourseTests
{
	private static readonly DateTimeOffset At = new(2026, 10, 5, 7, 20, 0, TimeSpan.FromHours(2));

	private static RunStop Stop(
		string id,
		string name,
		double latitude,
		double longitude,
		int minutesFromStart = 0) =>
		new()
		{
			Station = new Station
			{
				Id = id,
				Name = name,
				Latitude = latitude,
				Longitude = longitude
			},
			Scheduled = At.AddMinutes(minutesFromStart)
		};

	private static Departure Dep(
		string line,
		string directionId,
		string destination,
		string id,
		string stateless = "") =>
		new()
		{
			Id = id,
			StopId = "33000028",
			Line = new TransitLine
			{
				Name = line,
				Mode = TransitMode.Tram,
				DirectionId = directionId,
				Destination = destination
			},
			Scheduled = At,
			ProviderData = stateless.Length > 0 ? stateless : null
		};

	private static RunDetail Window(params RunStop[] stops) =>
		new(stops)
		{
			Path = [(51.03, 13.73), (51.05, 13.75), (51.07, 13.77)]
		};

	[Fact]
	public void The_window_repeats_a_station_once_per_chained_journey()
	{
		RunStop[] window =
		[
			Stop("a", "Prohlis", 51.00, 13.75),
			Stop("b", "Waschplan", 51.02, 13.76, 3),
			Stop("c", "Bühlau", 51.05, 13.80, 9),
			Stop("b", "Waschplan", 51.02, 13.76, 40),   // the chained journey turns around: same station id
			Stop("a", "Prohlis", 51.00, 13.75, 50)
		];

		IReadOnlyList<RunStop> deduplicated = LineCourseService.Deduplicate(window);

		Assert.Equal(3, deduplicated.Count);
		Assert.Equal("a", deduplicated[0].Station.Id);
		Assert.Equal("b", deduplicated[1].Station.Id);
		Assert.Equal("c", deduplicated[2].Station.Id);
	}

	[Fact]
	public void Without_ids_the_rounded_position_and_the_name_stand_in()
	{
		RunStop[] window =
		[
			Stop("", "Postplatz", 51.05123, 13.74001),
			Stop("", "Altmarkt", 51.054, 13.745, 4),
			Stop("", "Postplatz", 51.05123, 13.74001, 30),   // same place, ~1 m key
			Stop("", "Postplatz", 51.09, 13.74, 50)          // same name, elsewhere: a stop of its own
		];

		IReadOnlyList<RunStop> deduplicated = LineCourseService.Deduplicate(window);

		Assert.Equal(3, deduplicated.Count);
	}

	[Fact]
	public void The_direction_letter_comes_from_a_stateless_id_or_the_line()
	{
		Assert.Equal("H", LineCourseService.DirectionOf(Dep("8", "R", "Bühlau", "x", stateless: "voe:11003: :H:j26")));
		Assert.Equal("R", LineCourseService.DirectionOf(Dep("8", "R", "Bühlau", "x")));
		Assert.Null(LineCourseService.DirectionOf(Dep("8", "", "Bühlau", "x")));
		Assert.Null(LineCourseService.DirectionOf(Dep("8", "", "Bühlau", "x", stateless: "not a stateless id")));
	}

	[Fact]
	public void Line_names_with_or_without_spaces_are_the_same_line()
	{
		Assert.True(LineCourseService.SameLine("S 1", "S1"));
		Assert.True(LineCourseService.SameLine("11", "11"));
		Assert.False(LineCourseService.SameLine("11", "111"));
		Assert.False(LineCourseService.SameLine(null, "11"));
	}

	[Fact]
	public void A_run_detail_becomes_a_course_with_the_window_s_termini()
	{
		RunDetail window = Window(
			Stop("a", "Prohlis", 51.00, 13.75),
			Stop("b", "Waschplan", 51.02, 13.76, 3),
			Stop("c", "Bühlau", 51.05, 13.80, 9));
		window = window with { Vehicle = new GeoPosition { Latitude = 51.01, Longitude = 13.76 } };

		LineCourse? course = LineCourseService.FromDetail(Dep("8", "H", "Bühlau", "x"), window);

		Assert.NotNull(course);
		Assert.Equal("8", course!.LineName);
		Assert.Equal(TransitMode.Tram, course.Mode);
		Assert.Equal("H", course.Direction);
		Assert.Equal(3, course.Stops.Count);
		Assert.Equal(3, course.Path.Count);
		Assert.Equal("Prohlis", course.FirstTerminus);
		Assert.Equal("Bühlau", course.LastTerminus);
		Assert.NotNull(course.Vehicle);
	}

	[Fact]
	public void A_window_without_positions_or_a_path_draws_nothing()
	{
		Assert.Null(LineCourseService.FromDetail(Dep("8", "H", "Bühlau", "x"), new RunDetail([])));

		RunDetail bare = Window(Stop("a", "Prohlis", 51.00, 13.75)) with { Path = [] };

		Assert.NotNull(LineCourseService.FromDetail(Dep("8", "H", "Bühlau", "x"), bare));   // one position, no path: a polyline of the stops exists
	}

	[Fact]
	public async Task Both_directions_are_told_apart_by_their_letters()
	{
		FakeDepartures provider = new(
		[
			Dep("8", "H", "Bühlau", "run-h"),
			Dep("8", "H", "Bühlau", "run-h-later", stateless: "voe:11008: :H:j26"),
			Dep("8", "R", "Prohlis", "run-r"),
			Dep("66", "H", "Niederwartha", "run-other-line")
		]);

		LineCourseService service = new(new DepartureService([provider]));

		IReadOnlyList<LineCourse> courses =
			await service.BothDirectionsAsync(
				new TransitLine { Name = "8", Mode = TransitMode.Tram },
				new Station { Id = "33000028", Name = "Postplatz" });

		Assert.Equal(2, courses.Count);
		Assert.Equal("H", courses[0].Direction);
		Assert.Equal("R", courses[1].Direction);
		Assert.Equal(["run-h", "run-r"], provider.RequestedRuns);
		Assert.Equal(30, provider.LastQuery?.Limit);
	}

	[Fact]
	public async Task Without_a_second_direction_the_first_stands_alone()
	{
		FakeDepartures provider = new([Dep("8", "H", "Bühlau", "run-h")]);

		LineCourseService service = new(new DepartureService([provider]));

		IReadOnlyList<LineCourse> courses =
			await service.BothDirectionsAsync(
				new TransitLine { Name = "8", Mode = TransitMode.Tram },
				new Station { Id = "33000028", Name = "Postplatz" });

		Assert.Single(courses);
		Assert.Equal("H", courses[0].Direction);
	}

	[Fact]
	public async Task A_board_without_the_line_answers_nothing()
	{
		FakeDepartures provider = new([Dep("66", "H", "Niederwartha", "run-66")]);

		LineCourseService service = new(new DepartureService([provider]));

		IReadOnlyList<LineCourse> courses =
			await service.BothDirectionsAsync(
				new TransitLine { Name = "8", Mode = TransitMode.Tram },
				new Station { Id = "33000028", Name = "Postplatz" });

		Assert.Empty(courses);
		Assert.Empty(provider.RequestedRuns);
	}

	/// <summary>A departure provider whose board and runs the test fills in.</summary>
	private sealed class FakeDepartures(List<Departure> board) : IDepartureProvider
	{
		public DepartureQuery? LastQuery { get; private set; }

		public List<string> RequestedRuns { get; } = [];

		public Task<DepartureBoard> GetDeparturesAsync(
			DepartureQuery query,
			CancellationToken cancellationToken = default)
		{
			LastQuery = query;

			return Task.FromResult(
				new DepartureBoard
				{
					StopName = query.Stop.Name,
					Departures = board
				});
		}

		public Task<IReadOnlyList<RunStop>> GetRunAsync(
			Departure departure,
			int timeoutSeconds = 15,
			CancellationToken cancellationToken = default)
		{
			RequestedRuns.Add(departure.Id);

			return Task.FromResult<IReadOnlyList<RunStop>>(
			[
				Stop("a", "Prohlis", 51.00, 13.75),
				Stop("b", "Waschplan", 51.02, 13.76, 3),
				Stop("c", "Bühlau", 51.05, 13.80, 9)
			]);
		}

		public Task<RunDetail> GetRunDetailAsync(
			Departure departure,
			int timeoutSeconds = 15,
			CancellationToken cancellationToken = default)
		{
			RequestedRuns.Add(departure.Id);

			RunDetail detail = Window(
				Stop("a", "Prohlis", 51.00, 13.75),
				Stop("b", "Waschplan", 51.02, 13.76, 3),
				Stop("c", "Bühlau", 51.05, 13.80, 9));

			return Task.FromResult(detail);
		}
	}
}
