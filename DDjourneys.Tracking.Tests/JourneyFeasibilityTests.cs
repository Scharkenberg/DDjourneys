using DDjourneys.Core.Models;

namespace DDjourneys.Tracking.Tests;

public sealed class JourneyFeasibilityTests
{
	private static readonly TimeSpan Zone = TimeSpan.FromHours(2);
	private static readonly Station A = new() { Id = "1", Name = "Hauptbahnhof" };
	private static readonly Station B = new() { Id = "2", Name = "Postplatz" };
	private static readonly Station C = new() { Id = "3", Name = "Schweriner Straße" };

	private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 3, hour, minute, 0, Zone);

	private static StopTime Stop(Station station, bool arrivalCancelled = false, bool departureCancelled = false, bool cancelled = false) =>
		new()
		{
			Station = station,
			IsArrivalCancelled = arrivalCancelled,
			IsDepartureCancelled = departureCancelled,
			IsCancelled = cancelled || arrivalCancelled || departureCancelled
		};

	private static JourneyLeg Ride(
		Station from,
		Station to,
		DateTimeOffset dep,
		DateTimeOffset arr,
		StopTime? first = null,
		StopTime? last = null,
		DateTimeOffset? liveDep = null,
		DateTimeOffset? liveArr = null,
		bool cancelled = false) =>
		new()
		{
			Mode = TransitMode.Tram,
			From = from,
			To = to,
			Line = new TransitLine { Name = "9", Mode = TransitMode.Tram },
			ScheduledDeparture = dep,
			ScheduledArrival = arr,
			RealtimeDeparture = liveDep,
			RealtimeArrival = liveArr,
			Stops = [first ?? Stop(from), last ?? Stop(to)],
			IsCancelled = cancelled
		};

	private static Journey Of(params JourneyLeg[] legs) => new() { From = legs[0].From, To = legs[^1].To, Legs = legs };

	[Fact]
	public void A_normal_journey_is_possible()
	{
		Journey journey = Of(Ride(A, B, At(17, 21), At(17, 27)), Ride(B, C, At(17, 32), At(17, 36)));

		Assert.Null(journey.Block);
		Assert.False(journey.IsImpossible);
	}

	[Fact]
	public void An_unserved_destination_stop_makes_the_journey_impossible()
	{
		Journey journey =
			Of(
				Ride(A, B, At(17, 21), At(17, 27)),
				Ride(B, C, At(17, 32), At(17, 36), last: Stop(C, arrivalCancelled: true)));

		JourneyBlock block = Assert.IsType<JourneyBlock>(journey.Block);

		Assert.Equal(JourneyBlockKind.AlightingNotServed, block.Kind);
		Assert.Equal(1, block.LegIndex);
		Assert.Equal("Schweriner Straße", block.Stop?.Name);
		Assert.False(journey.IsCancelled);
	}

	[Fact]
	public void An_unserved_boarding_stop_makes_the_journey_impossible()
	{
		Journey journey = Of(Ride(A, B, At(17, 21), At(17, 27), first: Stop(A, departureCancelled: true)));

		Assert.Equal(JourneyBlockKind.BoardingNotServed, journey.Block?.Kind);
	}

	[Fact]
	public void A_trip_cut_short_at_the_alighting_stop_is_still_possible()
	{
		// The vehicle ends early exactly where one gets off: only its departure is cancelled there.
		Journey journey = Of(Ride(A, B, At(17, 21), At(17, 27), last: Stop(B, departureCancelled: true)));

		Assert.Null(journey.Block);
	}

	[Fact]
	public void A_provider_without_directional_flags_blocks_both_ways()
	{
		Journey journey = Of(Ride(A, B, At(17, 21), At(17, 27), last: Stop(B, cancelled: true)));

		Assert.Equal(JourneyBlockKind.AlightingNotServed, journey.Block?.Kind);
	}

	[Fact]
	public void A_cancelled_ride_is_reported_first()
	{
		Journey journey = Of(Ride(A, B, At(17, 21), At(17, 27), cancelled: true));

		Assert.Equal(JourneyBlockKind.RideCancelled, journey.Block?.Kind);
		Assert.True(journey.IsCancelled);
	}

	[Fact]
	public void A_connection_is_broken_when_the_next_vehicle_leaves_before_the_first_arrives()
	{
		Journey journey =
			Of(
				Ride(A, B, At(17, 21), At(17, 27), liveArr: At(17, 34)),
				Ride(B, C, At(17, 32), At(17, 36)));

		JourneyBlock block = Assert.IsType<JourneyBlock>(journey.Block);

		Assert.Equal(JourneyBlockKind.ConnectionBroken, block.Kind);
		Assert.Equal(1, block.LegIndex);
	}

	[Fact]
	public void A_tight_but_reachable_connection_is_not_broken()
	{
		Journey journey =
			Of(
				Ride(A, B, At(17, 21), At(17, 27), liveArr: At(17, 31)),
				Ride(B, C, At(17, 32), At(17, 36)));

		Assert.Null(journey.Block);
	}
}
