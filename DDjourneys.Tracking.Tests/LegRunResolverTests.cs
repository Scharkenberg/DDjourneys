using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>Finding the departure behind a journey leg, and the course of its whole run.</summary>
public sealed class LegRunResolverTests
{
	private static readonly DateTimeOffset At = new(2026, 10, 5, 7, 20, 0, TimeSpan.FromHours(2));

	private static Station Stop(string id, string name, double? lat = null, double? lon = null) =>
		new() { Id = id, Name = name, Latitude = lat, Longitude = lon };

	private static JourneyLeg Leg(string? destination) =>
		new()
		{
			Mode = TransitMode.Tram,
			From = Stop("1", "Kirschenstraße"),
			To = Stop("2", "Postplatz"),
			Line = new TransitLine { Name = "7", Mode = TransitMode.Tram, Destination = destination },
			ScheduledDeparture = At
		};

	private static Departure Dep(string line, string destination, TimeSpan offset) =>
		new()
		{
			Id = $"{line}-{destination}-{offset}",
			StopId = "1",
			Line = new TransitLine { Name = line, Mode = TransitMode.Tram, Destination = destination },
			Scheduled = At + offset
		};

	[Fact]
	public void The_departure_with_the_legs_destination_wins_over_a_closer_time()
	{
		Departure[] board =
		[
			Dep("7", "Gorbitz", TimeSpan.Zero),
			Dep("7", "Weixdorf", TimeSpan.FromSeconds(60)),
			Dep("3", "Weixdorf", TimeSpan.Zero)
		];

		Assert.Equal("Weixdorf", LegRunResolver.Pick(board, Leg("Weixdorf"), "7")?.Line.Destination);
		Assert.Equal("Gorbitz", LegRunResolver.Pick(board, Leg(null), "7")?.Line.Destination);
	}

	[Fact]
	public void Another_line_or_another_time_is_not_the_leg()
	{
		Departure[] board =
		[
			Dep("3", "Weixdorf", TimeSpan.Zero),
			Dep("7", "Weixdorf", TimeSpan.FromMinutes(10))
		];

		Assert.Null(LegRunResolver.Pick(board, Leg("Weixdorf"), "7"));
	}

	[Fact]
	public void The_whole_run_becomes_the_course_including_the_stops_before_the_boarding_stop()
	{
		Departure departure = Dep("7", "Weixdorf", TimeSpan.Zero);

		static RunStop Run(string id, double lat, TimeSpan offset, RunPosition position) =>
			new()
			{
				Station = Stop(id, id, lat, 13.7),
				Position = position,
				Scheduled = At + offset
			};

		RunStop[] stops =
		[
			Run("a", 51.00, TimeSpan.FromMinutes(-6), RunPosition.Previous),
			Run("b", 51.01, TimeSpan.FromMinutes(-3), RunPosition.Previous),
			Run("1", 51.02, TimeSpan.Zero, RunPosition.Current),
			Run("c", 51.03, TimeSpan.FromMinutes(3), RunPosition.Onward)
		];

		TrackTarget? target = LegRunResolver.FromRun(stops, departure, Leg("Weixdorf"), "7");

		Assert.NotNull(target);
		Assert.Equal(4, target.Course.Count);
		Assert.Equal("a", target.Course[0].Name);
		Assert.Equal("Weixdorf", target.Direction);
	}

	[Fact]
	public void A_run_without_positions_is_no_course()
	{
		Departure departure = Dep("7", "Weixdorf", TimeSpan.Zero);

		RunStop[] stops =
		[
			new() { Station = Stop("1", "x"), Position = RunPosition.Current, Scheduled = At },
			new() { Station = Stop("2", "y"), Position = RunPosition.Onward, Scheduled = At.AddMinutes(2) }
		];

		Assert.Null(LegRunResolver.FromRun(stops, departure, Leg(null), "7"));
	}
}
