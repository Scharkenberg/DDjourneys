using DDjourneys.Core.Theming;

namespace DDjourneys.Tracking.Tests;

/// <summary>Sunrise and sunset (NOAA equations) for the sun-following theme: Dresden reference days, polar belt, margins.</summary>
public sealed class SunTimesTests
{
	private const double DresdenLat = 51.0504;
	private const double DresdenLon = 13.7373;

	[Fact]
	public void Dresden_matches_the_reference_days()
	{
		// Reference values from the same NOAA equations (minutes UTC; three minutes of slack for rounding).
		AssertDay(new(2026, 3, 20), "05:10", "17:16");
		AssertDay(new(2026, 6, 21), "02:50", "19:23");
		AssertDay(new(2026, 9, 23), "04:51", "17:04");
		AssertDay(new(2026, 12, 21), "07:06", "15:00");
	}

	[Fact]
	public void The_polar_belt_has_no_events()
	{
		(DateTimeOffset? up, DateTimeOffset? down) = SunTimes.For(69.65, 18.96, new DateOnly(2026, 6, 21));
		Assert.Null(up);
		Assert.Null(down);

		(up, down) = SunTimes.For(69.65, 18.96, new DateOnly(2026, 12, 21));
		Assert.Null(up);
		Assert.Null(down);
	}

	[Fact]
	public void Daylight_switches_around_the_margin()
	{
		var margin = TimeSpan.FromMinutes(20);
		DateTimeOffset sunrise = new(2026, 6, 21, 2, 50, 0, TimeSpan.Zero);
		DateTimeOffset sunset = new(2026, 6, 21, 19, 23, 0, TimeSpan.Zero);

		Assert.False(SunTimes.IsDaylight(DresdenLat, DresdenLon, sunrise - TimeSpan.FromMinutes(25), margin));
		Assert.True(SunTimes.IsDaylight(DresdenLat, DresdenLon, sunrise - TimeSpan.FromMinutes(15), margin));
		Assert.True(SunTimes.IsDaylight(DresdenLat, DresdenLon, sunset + TimeSpan.FromMinutes(15), margin));
		Assert.False(SunTimes.IsDaylight(DresdenLat, DresdenLon, sunset + TimeSpan.FromMinutes(25), margin));
		Assert.True(SunTimes.IsDaylight(DresdenLat, DresdenLon, new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero), margin));
		Assert.False(SunTimes.IsDaylight(DresdenLat, DresdenLon, new DateTimeOffset(2026, 6, 21, 23, 30, 0, TimeSpan.Zero), margin));
	}

	[Fact]
	public void Polar_days_and_nights_decide_by_declination()
	{
		var margin = TimeSpan.FromMinutes(20);

		Assert.True(SunTimes.IsDaylight(69.65, 18.96, new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero), margin));
		Assert.False(SunTimes.IsDaylight(69.65, 18.96, new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.Zero), margin));
	}

	[Fact]
	public void The_next_boundary_wraps_past_midnight()
	{
		var slack = TimeSpan.FromMinutes(3);

		// Before sunrise: today's sunrise. After sunset: tomorrow's sunrise.
		Assert.Equal(
			new DateTimeOffset(2026, 6, 21, 2, 50, 0, TimeSpan.Zero),
			SunTimes.NextBoundary(DresdenLat, DresdenLon, new DateTimeOffset(2026, 6, 21, 1, 0, 0, TimeSpan.Zero))!.Value,
			slack);

		Assert.Equal(
			new DateTimeOffset(2026, 6, 22, 2, 50, 0, TimeSpan.Zero),
			SunTimes.NextBoundary(DresdenLat, DresdenLon, new DateTimeOffset(2026, 6, 21, 21, 0, 0, TimeSpan.Zero))!.Value,
			slack);

		// Between the events: the sunset of the same day.
		Assert.Equal(
			new DateTimeOffset(2026, 6, 21, 19, 23, 0, TimeSpan.Zero),
			SunTimes.NextBoundary(DresdenLat, DresdenLon, new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero))!.Value,
			slack);
	}

	private static void AssertDay(DateOnly day, string sunrise, string sunset)
	{
		(DateTimeOffset? up, DateTimeOffset? down) = SunTimes.For(DresdenLat, DresdenLon, day);

		Assert.NotNull(up);
		Assert.NotNull(down);
		Assert.Equal(TimeOfDay(day, sunrise), up!.Value, TimeSpan.FromMinutes(3));
		Assert.Equal(TimeOfDay(day, sunset), down!.Value, TimeSpan.FromMinutes(3));
	}

	private static DateTimeOffset TimeOfDay(DateOnly day, string clock) =>
		new(day.ToDateTime(TimeOnly.ParseExact(clock, "HH:mm")), TimeSpan.Zero);
}
