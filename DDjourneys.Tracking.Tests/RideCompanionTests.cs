using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking.Live;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The pure logic of the ride companion (B5): stay in the vehicle, tight at your pace, the get-off alarm
/// lead, the stops-to-go countdown and the position snapping with its freshness rule.
/// </summary>
public sealed class RideCompanionTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

	// ----- Stay in the vehicle -----

	[Fact]
	public void Stay_in_vehicle_matches_the_destination_by_stop_id()
	{
		List<CourseStop> run =
		[
			new("voe:1", "Löbtau", Now.AddMinutes(-4)),
			new("voe:2", "Schweriner Straße", Now.AddMinutes(-2)),
			new("voe:3", "Hauptbahnhof", Now.AddMinutes(2)),
			new("voe:4", "Albertplatz", Now.AddMinutes(5))
		];

		StayInVehicle.Hint? hint =
			StayInVehicle.After(run, "voe:2", "Schweriner Straße", "voe:4", "Albertplatz");

		Assert.NotNull(hint);
		Assert.Equal(3, hint!.Index);
		Assert.Equal("Albertplatz", hint.Name);
		Assert.Equal(Now.AddMinutes(5), hint.At);
		Assert.True(hint.ById);
	}

	[Fact]
	public void Stay_in_vehicle_falls_back_to_normalized_names()
	{
		List<CourseStop> run =
		[
			new(null, "Löbtau", null),
			new(null, "Schweriner Straße", null),
			new(null, "Albertplatz", Now.AddMinutes(5))
		];

		StayInVehicle.Hint? hint =
			StayInVehicle.After(run, null, "schweriner straße", null, "albertplatz ");

		Assert.NotNull(hint);
		Assert.Equal(Now.AddMinutes(5), hint!.At);
		Assert.False(hint.ById);
	}

	[Fact]
	public void Stay_in_vehicle_never_matches_a_partial_name()
	{
		List<CourseStop> run =
		[
			new(null, "Hauptbahnhof", null),
			new(null, "Albertplatz", null),
			new(null, "Hauptstraße", null)
		];

		Assert.Null(StayInVehicle.After(run, null, "Albertplatz", null, "Haupt"));
		Assert.Null(StayInVehicle.After(run, null, "Albertplatz", null, "bahnhof"));
	}

	[Fact]
	public void Stay_in_vehicle_requires_the_destination_after_the_transfer_stop()
	{
		List<CourseStop> run =
		[
			new(null, "Albertplatz", null),
			new(null, "Schweriner Straße", null),
			new(null, "Löbtau", null)
		];

		// The destination is only served BEFORE the transfer stop: stay silent.
		Assert.Null(StayInVehicle.After(run, null, "Schweriner Straße", null, "Albertplatz"));
	}

	[Fact]
	public void Stay_in_vehicle_stays_silent_when_the_run_does_not_serve_the_transfer_stop()
	{
		List<CourseStop> run =
		[
			new(null, "Löbtau", null),
			new(null, "Albertplatz", null)
		];

		Assert.Null(StayInVehicle.After(run, null, "Bühlau", null, "Albertplatz"));
	}

	[Fact]
	public void Stay_in_vehicle_prefers_the_stop_id_over_the_name()
	{
		List<CourseStop> run =
		[
			new("voe:2", "Schweriner Straße", null),
			new("voe:9", "Albertplatz", null)
		];

		// Both carry ids, and they differ: the same name must not make it a match.
		Assert.Null(StayInVehicle.After(run, "voe:2", "Schweriner Straße", "voe:4", "Albertplatz"));
	}

	[Fact]
	public void Stay_in_vehicle_omits_the_time_when_the_schedule_carries_none()
	{
		List<CourseStop> run =
		[
			new(null, "Löbtau", Now),
			new(null, "Albertplatz", null)
		];

		StayInVehicle.Hint? hint =
			StayInVehicle.After(run, null, "Löbtau", null, "Albertplatz");

		Assert.NotNull(hint);
		Assert.Null(hint!.At);
	}

	// ----- Tight at your pace -----

	[Fact]
	public void Pace_factor_ladder_changes_a_quarter_per_step()
	{
		Assert.Equal(1.5625, JourneyFeasibility.PaceFactor(WalkingPace.VerySlow));
		Assert.Equal(1.25, JourneyFeasibility.PaceFactor(WalkingPace.Slow));
		Assert.Equal(1.0, JourneyFeasibility.PaceFactor(WalkingPace.Normal));
		Assert.Equal(0.8, JourneyFeasibility.PaceFactor(WalkingPace.Fast));
		Assert.Equal(0.64, JourneyFeasibility.PaceFactor(WalkingPace.VeryFast));
	}

	[Fact]
	public void Walk_at_pace_scales_the_quoted_duration()
	{
		TimeSpan quoted = TimeSpan.FromMinutes(4);

		Assert.Equal(TimeSpan.FromMinutes(5), JourneyFeasibility.WalkAtPace(quoted, WalkingPace.Slow));
		Assert.Equal(quoted, JourneyFeasibility.WalkAtPace(quoted, WalkingPace.Normal));
		Assert.Equal(TimeSpan.FromSeconds(192), JourneyFeasibility.WalkAtPace(quoted, WalkingPace.Fast));
	}

	[Fact]
	public void A_change_that_needs_a_faster_pace_is_tight_at_the_users_pace()
	{
		// The quoted walk of 4 minutes fills the whole window (no wait left); at a slow pace it needs 5.
		Assert.True(
			JourneyFeasibility.IsTightAtPace(
				TimeSpan.FromMinutes(4),
				TimeSpan.Zero,
				WalkingPace.Slow));
	}

	[Fact]
	public void A_comfortable_change_stays_comfortable_at_the_users_pace()
	{
		Assert.False(
			JourneyFeasibility.IsTightAtPace(
				TimeSpan.FromMinutes(2),
				TimeSpan.FromMinutes(6),
				WalkingPace.Slow));

		// No walk at all: nothing to be tight about.
		Assert.False(
			JourneyFeasibility.IsTightAtPace(
				null,
				TimeSpan.FromMinutes(1),
				WalkingPace.VerySlow));
	}

	[Fact]
	public void Normal_and_faster_paces_are_never_tight_at_the_users_pace()
	{
		Assert.False(
			JourneyFeasibility.IsTightAtPace(
				TimeSpan.FromMinutes(4),
				TimeSpan.Zero,
				WalkingPace.Normal));

		Assert.False(
			JourneyFeasibility.IsTightAtPace(
				TimeSpan.FromMinutes(4),
				TimeSpan.Zero,
				WalkingPace.VeryFast));
	}

	[Fact]
	public void Half_a_minute_of_difference_is_rounding_not_tightness()
	{
		// At a slow pace the walk needs exactly as long as the window allows (2.5 of 2.5 minutes): not tight.
		Assert.False(
			JourneyFeasibility.IsTightAtPace(
				TimeSpan.FromMinutes(2),
				TimeSpan.FromSeconds(30),
				WalkingPace.Slow));
	}

	// ----- Get-off alarm -----

	[Fact]
	public void The_get_off_alarm_offers_off_and_one_to_three_minutes()
	{
		Assert.Equal([0, 1, 2, 3], GetOffAlarm.LeadChoices);
		Assert.Equal(1, GetOffAlarm.DefaultLead);
	}

	[Theory]
	[InlineData(-3, 0)]
	[InlineData(2, 2)]
	[InlineData(9, 3)]
	public void The_get_off_alarm_lead_is_brought_back_into_the_offered_range(int stored, int expected) =>
		Assert.Equal(expected, GetOffAlarm.Lead(stored));

	[Fact]
	public void The_get_off_alarm_is_due_within_the_lead_time()
	{
		DateTimeOffset alighting = Now.AddMinutes(2);

		Assert.True(GetOffAlarm.Due(alighting, 3, Now));
		Assert.False(GetOffAlarm.Due(alighting, 1, Now));
		Assert.True(GetOffAlarm.Due(Now.AddMinutes(1), 1, Now));
	}

	[Fact]
	public void The_get_off_alarm_is_due_once_the_stop_has_just_been_reached()
	{
		Assert.True(GetOffAlarm.Due(Now.AddSeconds(-20), 1, Now));
	}

	[Fact]
	public void A_switched_off_alarm_never_rings_and_neither_does_one_without_a_time()
	{
		Assert.False(GetOffAlarm.Due(Now, 0, Now));
		Assert.False(GetOffAlarm.Due(null, 3, Now));
	}

	// ----- Stops to go -----

	[Fact]
	public void The_countdown_counts_the_stops_ahead_and_the_exit_from_the_schedule()
	{
		List<RideCountdowns.StopVisit> stops =
		[
			new("Gothaer Straße", Now.AddMinutes(-4)),
			new("Schweriner Straße", Now.AddMinutes(-2)),
			new("Hauptbahnhof", Now.AddMinutes(1)),
			new("Albertplatz", Now.AddMinutes(4))
		];

		RideCountdown? countdown = RideCountdowns.Of(stops, Now);

		Assert.NotNull(countdown);
		Assert.Equal(2, countdown!.Stops);
		Assert.Equal(TimeSpan.FromMinutes(4), countdown.ExitIn);
	}

	[Fact]
	public void The_countdown_claims_no_time_when_the_ride_carries_none()
	{
		List<RideCountdowns.StopVisit> stops =
		[
			new("Nürnberger Platz", null),
			new("Hauptbahnhof", null),
			new("Albertplatz", null)
		];

		RideCountdown? countdown = RideCountdowns.Of(stops, Now);

		Assert.NotNull(countdown);
		Assert.Equal(3, countdown!.Stops);
		Assert.Null(countdown.ExitIn);
	}

	[Fact]
	public void The_countdown_estimates_from_the_rides_own_spacing_when_the_last_stop_has_no_time()
	{
		// The gaps are 2 and 4 minutes; the median of two is the upper one, 4. One stop remains.
		List<RideCountdowns.StopVisit> stops =
		[
			new("Löbtau", Now.AddMinutes(-6)),
			new("Schweriner Straße", Now.AddMinutes(-4)),
			new("Hauptbahnhof", Now),
			new("Albertplatz", null)
		];

		RideCountdown? countdown = RideCountdowns.Of(stops, Now);

		Assert.NotNull(countdown);
		Assert.Equal(1, countdown!.Stops);
		Assert.Equal(TimeSpan.FromMinutes(4), countdown.ExitIn);
	}

	[Fact]
	public void The_countdown_keeps_the_get_off_stop_when_its_time_has_just_passed()
	{
		List<RideCountdowns.StopVisit> stops =
		[
			new("Hauptbahnhof", Now.AddMinutes(-2)),
			new("Albertplatz", Now.AddSeconds(-30))
		];

		RideCountdown? countdown = RideCountdowns.Of(stops, Now);

		Assert.NotNull(countdown);
		Assert.Equal(1, countdown!.Stops);
		Assert.Equal(TimeSpan.FromSeconds(-30), countdown.ExitIn);
	}

	[Fact]
	public void The_countdown_of_an_empty_ride_is_null() =>
		Assert.Null(RideCountdowns.Of([], Now));

	// ----- Position snapping -----

	[Fact]
	public void A_position_is_projected_onto_the_nearest_segment()
	{
		List<(double Latitude, double Longitude)> path =
		[
			(51.0, 13.0),
			(51.0, 13.1),
			(51.1, 13.1)
		];

		// Beside the middle of the first segment (which runs east).
		(double Latitude, double Longitude)? snapped =
			PositionSnapping.Snap(51.001, 13.05, path);

		Assert.NotNull(snapped);
		Assert.Equal(51.0, snapped!.Value.Latitude, 6);
		Assert.Equal(13.05, snapped.Value.Longitude, 6);
	}

	[Fact]
	public void A_position_beyond_the_ends_clamps_to_the_path()
	{
		List<(double Latitude, double Longitude)> path = [(51.0, 13.0), (51.0, 13.1)];

		(double Latitude, double Longitude)? before =
			PositionSnapping.Snap(51.0, 12.9, path);

		(double Latitude, double Longitude)? after =
			PositionSnapping.Snap(51.0, 13.5, path);

		Assert.NotNull(before);
		Assert.Equal((51.0, 13.0), before!.Value);

		Assert.NotNull(after);
		Assert.Equal((51.0, 13.1), after!.Value);
	}

	[Fact]
	public void A_path_without_a_segment_has_nothing_to_snap_to() =>
		Assert.Null(PositionSnapping.Snap(51.0, 13.0, [(51.0, 13.0)]));

	[Fact]
	public void A_vehicle_position_is_fresh_within_the_vehicles_layer_window()
	{
		Assert.True(PositionSnapping.IsFresh(Now.AddSeconds(-90), Now));
		Assert.False(PositionSnapping.IsFresh(Now.AddSeconds(-91), Now));
		Assert.False(PositionSnapping.IsFresh(null, Now));
	}
}
