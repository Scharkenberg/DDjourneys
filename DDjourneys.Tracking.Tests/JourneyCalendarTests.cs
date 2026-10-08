using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

public sealed class JourneyCalendarTests
{
	private static Journey Trip(int minute)
	{
		var a = new Station { Id = "1", Name = "Alpha" };
		var b = new Station { Id = "2", Name = "Beta" };
		DateTimeOffset start = new(2026, 10, 5, 10, minute, 0, TimeSpan.FromHours(2));

		var leg = new JourneyLeg
		{
			Mode = TransitMode.Tram,
			From = a,
			To = b,
			Line = new TransitLine { Name = "11", Mode = TransitMode.Tram },
			ScheduledDeparture = start,
			ScheduledArrival = start.AddMinutes(20)
		};

		return new Journey { Legs = [leg], From = a, To = b };
	}

	[Fact]
	public void The_event_holds_times_in_utc_an_escaped_text_and_a_reminder()
	{
		string? file = JourneyCalendar.Build(Trip(0), "Alpha → Beta, now; later", "Line 1\nLine 2", 5, DateTimeOffset.UnixEpoch);

		Assert.NotNull(file);
		Assert.Contains("DTSTART:20261005T080000Z\r\n", file);
		Assert.Contains("DTEND:20261005T082000Z\r\n", file);
		Assert.Contains("SUMMARY:Alpha → Beta\\, now\\; later\r\n", file);
		Assert.Contains("DESCRIPTION:Line 1\\nLine 2\r\n", file);
		Assert.Contains("TRIGGER:-PT5M\r\n", file);
	}

	[Fact]
	public void The_same_journey_keeps_its_id_and_another_one_does_not()
	{
		Assert.Equal(JourneyCalendar.Uid(Trip(0)), JourneyCalendar.Uid(Trip(0)));
		Assert.NotEqual(JourneyCalendar.Uid(Trip(0)), JourneyCalendar.Uid(Trip(10)));
	}

	[Fact]
	public void Long_lines_are_folded_at_75_octets()
	{
		string file = JourneyCalendar.Build(Trip(0), "x", new string('ä', 200), 0, DateTimeOffset.UnixEpoch)!;

		Assert.All(file.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75));
	}
}
