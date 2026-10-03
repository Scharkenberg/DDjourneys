using DDjourneys.Core.Tracking;

namespace DDjourneys.Tracking.Tests;

public sealed class NoticePolicyTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Information_expires_after_its_age()
	{
		Assert.True(NoticePolicy.Evaluate(NoticeKind.Information, Now.AddMinutes(-29), false, false, Now).IsVisible);
		Assert.False(NoticePolicy.Evaluate(NoticeKind.Information, Now.AddMinutes(-31), false, false, Now).IsVisible);
	}

	[Fact]
	public void A_risk_notice_lives_while_the_risk_is_live_and_fades_afterwards()
	{
		Assert.True(NoticePolicy.Evaluate(NoticeKind.ConnectionRisk, Now.AddHours(-2), false, true, Now).IsVisible);
		Assert.True(NoticePolicy.Evaluate(NoticeKind.ConnectionRisk, Now.AddMinutes(-9), false, false, Now).IsVisible);
		Assert.False(NoticePolicy.Evaluate(NoticeKind.ConnectionRisk, Now.AddMinutes(-11), false, false, Now).IsVisible);
	}

	[Fact]
	public void A_cancellation_stays_until_the_journey_is_over()
	{
		Assert.True(NoticePolicy.Evaluate(NoticeKind.Cancellation, Now.AddHours(-3), false, false, Now).IsVisible);
		Assert.False(NoticePolicy.Evaluate(NoticeKind.Cancellation, Now.AddHours(-3), true, false, Now).IsVisible);
	}

	[Fact]
	public void Nothing_is_shown_once_the_journey_is_over()
	{
		foreach (NoticeKind kind in Enum.GetValues<NoticeKind>())
		{
			Assert.False(NoticePolicy.Evaluate(kind, Now, true, true, Now).IsVisible);
		}
	}

	[Fact]
	public void A_notice_without_a_time_does_not_age_out()
	{
		NoticeVisibility v = NoticePolicy.Evaluate(NoticeKind.Information, null, false, false, Now);

		Assert.True(v.IsVisible);
		Assert.Null(v.ExpiresAt);
	}
}

public sealed class InPlaceChangeTests
{
	private static TrackedSegment Walk(string from, string? fromPlatform, string to, string? toPlatform) =>
		new(
			true, null, null,
			[
				new TrackedStop(from, null, null, TrackedStopState.Upcoming, fromPlatform),
				new TrackedStop(to, null, null, TrackedStopState.Upcoming, toPlatform)
			],
			false, false);

	[Fact]
	public void Same_stop_and_same_platform_is_in_place()
	{
		Assert.True(Walk("Hauptbahnhof", "3", "hauptbahnhof ", "3").IsInPlaceChange);
	}

	[Fact]
	public void Same_stop_with_unknown_platforms_is_in_place()
	{
		Assert.True(Walk("Postplatz", null, "Postplatz", null).IsInPlaceChange);
		Assert.True(Walk("Postplatz", "A", "Postplatz", null).IsInPlaceChange);
	}

	[Fact]
	public void Same_stop_with_different_platforms_is_a_real_change()
	{
		Assert.False(Walk("Hauptbahnhof", "3", "Hauptbahnhof", "7").IsInPlaceChange);
	}

	[Fact]
	public void Different_stops_are_a_real_walk()
	{
		Assert.False(Walk("Hauptbahnhof", "3", "Prager Straße", null).IsInPlaceChange);
	}

	[Fact]
	public void A_ride_is_never_an_in_place_change()
	{
		var ride = new TrackedSegment(false, "11", null, Walk("A", null, "A", null).Stops, false, false);

		Assert.False(ride.IsInPlaceChange);
	}
}
