using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;

namespace DDjourneys.Tracking.Tests;

public sealed class LiveBackgroundRulesTests
{
	private static LiveJourneyContent Content(TrackingPhase phase, bool ongoing) =>
		new("plan", "T", "text", null, null, [], [], 0, phase, ongoing);

	[Fact]
	public void No_presentation_keeps_nothing_alive() =>
		Assert.False(LiveBackgroundRules.KeepsProcessAlive(null));

	[Theory]
	[InlineData(TrackingPhase.Planned)]
	[InlineData(TrackingPhase.InProgress)]
	[InlineData(TrackingPhase.AtInterchange)]
	[InlineData(TrackingPhase.AtRisk)]
	public void A_monitored_journey_keeps_the_process_alive(TrackingPhase phase) =>
		Assert.True(LiveBackgroundRules.KeepsProcessAlive(Content(phase, ongoing: true)));

	[Theory]
	[InlineData(TrackingPhase.Paused)]
	[InlineData(TrackingPhase.Arrived)]
	[InlineData(TrackingPhase.Cancelled)]
	public void A_finished_or_paused_journey_does_not(TrackingPhase phase) =>
		Assert.False(LiveBackgroundRules.KeepsProcessAlive(Content(phase, ongoing: true)));

	[Fact]
	public void A_presentation_that_is_not_ongoing_does_not() =>
		Assert.False(LiveBackgroundRules.KeepsProcessAlive(Content(TrackingPhase.InProgress, ongoing: false)));
}
