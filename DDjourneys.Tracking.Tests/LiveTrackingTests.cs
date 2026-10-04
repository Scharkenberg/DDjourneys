using System.Text.Json;
using DDjourneys.Core.Tracking;
using DDjourneys.Tracking.Schutzengel;

namespace DDjourneys.Tracking.Tests;

/// <summary>Choice of the live journey and the extended course view.</summary>
public sealed class LiveTrackingTests
{
	private static readonly DateTimeOffset Base = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

	// ----- Live owner -----

	[Fact]
	public void The_chosen_journey_owns_the_live_presentation_while_it_is_eligible()
	{
		string[] eligible = ["a", "b"];

		Assert.Equal("b", LivePlanSelector.Choose("b", eligible));
		Assert.Equal("a", LivePlanSelector.Choose("gone", eligible));
		Assert.Equal("a", LivePlanSelector.Choose(null, eligible));
		Assert.Null(LivePlanSelector.Choose("b", Array.Empty<string>()));
	}

	// ----- Course -----

	[Fact]
	public void During_a_ride_the_stops_are_behind_current_and_next()
	{
		TrackedTrip trip = Timeline().Describe("plan", Base.AddMinutes(7));

		Assert.Equal(3, trip.Segments.Count);

		TrackedSegment ride = trip.Segments[0];

		Assert.True(ride.IsCurrent);
		Assert.False(ride.IsWalk);
		Assert.Equal("11", ride.Line);
		Assert.Equal(
			new[] { TrackedStopState.Passed, TrackedStopState.Current, TrackedStopState.Next },
			ride.Stops.Select(stop => stop.State));

		Assert.All(trip.Segments.Skip(1), segment => Assert.False(segment.IsCurrent || segment.IsPassed));
		Assert.All(trip.Segments[2].Stops, stop => Assert.Equal(TrackedStopState.Upcoming, stop.State));
	}

	[Fact]
	public void Real_time_delays_are_kept_per_stop()
	{
		TrackedStop arrival = Timeline().Describe("plan", Base.AddMinutes(7)).Segments[0].Stops[^1];

		Assert.Equal("B", arrival.Name);
		Assert.Equal(TimeSpan.FromMinutes(2), arrival.Delay);
		Assert.Equal(Base.AddMinutes(12), arrival.Effective);
	}

	[Fact]
	public void During_a_change_the_walk_is_current_and_the_first_ride_is_behind()
	{
		TrackedTrip trip = Timeline().Describe("plan", Base.AddMinutes(15));

		Assert.True(trip.Segments[0].IsPassed);
		Assert.All(trip.Segments[0].Stops, stop => Assert.Equal(TrackedStopState.Passed, stop.State));

		TrackedSegment walk = trip.Segments[1];

		Assert.True(walk.IsWalk);
		Assert.True(walk.IsCurrent);
		Assert.Equal(new[] { TrackedStopState.Current, TrackedStopState.Next }, walk.Stops.Select(stop => stop.State));
	}

	[Fact]
	public void After_arrival_everything_is_behind()
	{
		TrackedTrip trip = Timeline().Describe("plan", Base.AddMinutes(40));

		Assert.All(trip.Segments, segment => Assert.True(segment.IsPassed && !segment.IsCurrent));
		Assert.All(trip.Segments.SelectMany(segment => segment.Stops), stop => Assert.Equal(TrackedStopState.Passed, stop.State));
	}

	[Fact]
	public void Before_departure_everything_is_ahead()
	{
		TrackedTrip trip = Timeline().Describe("plan", Base.AddMinutes(-5));

		Assert.All(trip.Segments.SelectMany(segment => segment.Stops), stop => Assert.Equal(TrackedStopState.Upcoming, stop.State));
	}

	[Fact]
	public void A_late_vehicle_is_not_arrived_at_its_planned_time()
	{
		static long Ms(double minutes) => Base.AddMinutes(minutes).ToUnixTimeMilliseconds();

		string json =
			$$"""
			{"data_version":1,"episodes":[
			 {"type":"public","mot":{"name":"2","direction":"Gorbitz"},
			  "from":{"name":"A","scheduledTime":{{Ms(0)}},"realtime":{{Ms(7)}} },
			  "to":{"name":"B","scheduledTime":{{Ms(14)}} },
			  "allStations":[
			   {"name":"A","scheduledTime":{{Ms(0)}},"realtime":{{Ms(7)}} },
			   {"name":"B","scheduledTime":{{Ms(14)}} }]}]}
			""";

		using JsonDocument document = JsonDocument.Parse(json);

		Assert.True(TripTimeline.TryParse(document.RootElement, null, out TripTimeline timeline));

		Assert.Equal(Base.AddMinutes(21), timeline.End);
		Assert.NotEqual(TripStage.Arrived, timeline.Calculate(Base.AddMinutes(15)).Stage);
	}

	[Fact]
	public void A_risk_notice_never_applies_to_an_ensured_change()
	{
		TripTimeline timeline = Timeline();
		const string notice = "Wegen Verspätungen von 0 Minute ist der geplante Umstieg an Haltestelle B gefährdet.";

		Assert.False(timeline.CoversEnsuredChange(notice));

		timeline.SetEnsured([true]);

		Assert.True(timeline.CoversEnsuredChange(notice));
		Assert.False(timeline.CoversEnsuredChange(null));

		timeline.SetEnsured([true, false]); // does not fit the rides: ignored
		Assert.False(timeline.CoversEnsuredChange(notice));
	}

	/// <summary>Ride 11 A(0) M(5) B(10, real-time 12), a walk at B, ride 7 B(20) C(35).</summary>
	private static TripTimeline Timeline()
	{
		static long Ms(double minutes) => Base.AddMinutes(minutes).ToUnixTimeMilliseconds();

		string json =
			$$"""
			{"data_version":1,"episodes":[
			 {"type":"public","mot":{"name":"11","direction":"Zschertnitz"},
			  "from":{"name":"A","scheduledTime":{{Ms(0)}} },
			  "to":{"name":"B","scheduledTime":{{Ms(10)}},"realtime":{{Ms(12)}} },
			  "allStations":[
			   {"name":"A","scheduledTime":{{Ms(0)}} },
			   {"name":"M","scheduledTime":{{Ms(5)}} },
			   {"name":"B","scheduledTime":{{Ms(10)}},"realtime":{{Ms(12)}} }]},
			 {"type":"individual","durationSeconds":180,
			  "from":{"name":"B","scheduledTime":{{Ms(10)}} },
			  "to":{"name":"B","scheduledTime":{{Ms(20)}} } },
			 {"type":"public","mot":{"name":"7","direction":"Pennrich"},
			  "from":{"name":"B","scheduledTime":{{Ms(20)}} },
			  "to":{"name":"C","scheduledTime":{{Ms(35)}} },
			  "allStations":[
			   {"name":"B","scheduledTime":{{Ms(20)}} },
			   {"name":"C","scheduledTime":{{Ms(35)}} }]}]}
			""";

		using JsonDocument document = JsonDocument.Parse(json);

		Assert.True(TripTimeline.TryParse(document.RootElement, null, out TripTimeline timeline));

		return timeline;
	}
}
