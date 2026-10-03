using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;

namespace DDjourneys.Tracking.Tests;

/// <summary>Decisions of surfaces that refresh their own countdown (Windows live notification).</summary>
public sealed class LivePresentationRulesTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

	private static LiveJourneyContent Content(
		string title = "T",
		int position = 0,
		IReadOnlyList<int>? segments = null,
		DateTimeOffset? when = null,
		TrackingPhase phase = TrackingPhase.InProgress,
		bool ongoing = true,
		string? subText = null) =>
		new(
			"plan",
			title,
			"text",
			subText,
			null,
			segments ?? [10, 20, 10],
			[false, true, false],
			position,
			phase,
			ongoing,
			when);

	// ----- Layout key -----

	[Fact]
	public void Layout_key_ignores_position_and_countdown_target()
	{
		string a = LivePresentationRules.LayoutKey(Content(position: 5, when: Now.AddMinutes(3)));
		string b = LivePresentationRules.LayoutKey(Content(position: 25, when: Now.AddMinutes(9)));

		Assert.Equal(a, b);
	}

	[Theory]
	[InlineData("title")]
	[InlineData("phase")]
	[InlineData("ongoing")]
	[InlineData("segments")]
	[InlineData("subtext")]
	public void Layout_key_changes_with_structure(string what)
	{
		LiveJourneyContent baseline = Content();

		LiveJourneyContent changed = what switch
		{
			"title" => Content(title: "other"),
			"phase" => Content(phase: TrackingPhase.AtRisk),
			"ongoing" => Content(ongoing: false),
			"segments" => Content(segments: [10, 20]),
			_ => Content(subText: "sub")
		};

		Assert.NotEqual(LivePresentationRules.LayoutKey(baseline), LivePresentationRules.LayoutKey(changed));
	}

	// ----- Progress -----

	[Theory]
	[InlineData(0, 0.0)]
	[InlineData(20, 0.5)]
	[InlineData(40, 1.0)]
	[InlineData(99, 1.0)]
	[InlineData(-5, 0.0)]
	public void Progress_is_position_along_the_summed_segments(int position, double expected) =>
		Assert.Equal(expected, LivePresentationRules.ProgressValue(Content(position: position)), 6);

	[Fact]
	public void Progress_without_segments_is_zero() =>
		Assert.Equal(0, LivePresentationRules.ProgressValue(Content(position: 5, segments: [])));

	// ----- Countdown -----

	[Fact]
	public void Remaining_is_null_without_a_target() =>
		Assert.Null(LivePresentationRules.Remaining(Content(), Now));

	[Fact]
	public void Remaining_is_negative_after_the_target() =>
		Assert.Equal(
			TimeSpan.FromMinutes(-2),
			LivePresentationRules.Remaining(Content(when: Now.AddMinutes(-2)), Now));

	[Theory]
	[InlineData(300, 5)]
	[InlineData(240.5, 5)]
	[InlineData(60, 1)]
	[InlineData(59, 1)]
	[InlineData(1, 1)]
	[InlineData(0, 0)]
	[InlineData(-1, -1)]
	[InlineData(-60, -1)]
	[InlineData(-61, -2)]
	public void Minutes_round_away_from_zero(double seconds, int expected) =>
		Assert.Equal(expected, LivePresentationRules.Minutes(TimeSpan.FromSeconds(seconds)));

	// ----- Tick delay -----

	[Theory]
	[InlineData(300)]
	[InlineData(299.9)]
	[InlineData(61)]
	[InlineData(59.5)]
	[InlineData(30)]
	[InlineData(0.2)]
	[InlineData(-0.2)]
	[InlineData(-45)]
	[InlineData(-60)]
	[InlineData(-3600)]
	public void The_delay_stays_within_the_resync_bounds_and_crosses_a_minute_boundary(double seconds)
	{
		var remaining = TimeSpan.FromSeconds(seconds);
		TimeSpan delay = LivePresentationRules.NextTickDelay(remaining);

		Assert.InRange(delay, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));

		// Not clamped to the 1 s floor: the displayed minute must differ once the delay has passed.
		if (delay > TimeSpan.FromSeconds(1) && delay < TimeSpan.FromSeconds(60))
		{
			Assert.NotEqual(
				LivePresentationRules.Minutes(remaining),
				LivePresentationRules.Minutes(remaining - delay));
		}
	}

	[Fact]
	public void A_far_target_resyncs_at_least_once_a_minute() =>
		Assert.True(LivePresentationRules.NextTickDelay(TimeSpan.FromHours(3)) <= TimeSpan.FromSeconds(60));
}
