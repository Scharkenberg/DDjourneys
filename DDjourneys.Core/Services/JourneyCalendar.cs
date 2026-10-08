using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>
/// A journey as an iCalendar file (RFC 5545): one event from the start of the journey (walk included) to its arrival,
/// the stops and lines in the description, a reminder before the start. The event id is derived from what the journey is
/// (stops, lines, planned times), so importing the same journey twice, or again after a delay, updates the event
/// instead of making another one.
/// </summary>
public static class JourneyCalendar
{
	public const string MediaType = "text/calendar";

	/// <summary>Null when the journey has no start or arrival time.</summary>
	public static string? Build(
		Journey journey,
		string summary,
		string description,
		int reminderMinutes,
		DateTimeOffset stamp)
	{
		ArgumentNullException.ThrowIfNull(journey);

		if (journey.Departure is not { } start
			|| journey.Arrival is not { } end
			|| end <= start)
		{
			return null;
		}

		string location = (journey.Origin ?? journey.From).Name;

		var lines = new List<string>
		{
			"BEGIN:VCALENDAR",
			"VERSION:2.0",
			"PRODID:-//DDjourneys//Journey//EN",
			"CALSCALE:GREGORIAN",
			"METHOD:PUBLISH",
			"BEGIN:VEVENT",
			$"UID:{Uid(journey)}@ddjourneys",
			$"DTSTAMP:{Utc(stamp)}",
			$"DTSTART:{Utc(start)}",
			$"DTEND:{Utc(end)}",
			$"SUMMARY:{Escape(summary)}",
			$"LOCATION:{Escape(location)}",
			$"DESCRIPTION:{Escape(description)}",
			"TRANSP:OPAQUE",
			"STATUS:CONFIRMED"
		};

		if (reminderMinutes > 0)
		{
			lines.Add("BEGIN:VALARM");
			lines.Add("ACTION:DISPLAY");
			lines.Add($"DESCRIPTION:{Escape(summary)}");
			lines.Add($"TRIGGER:-PT{reminderMinutes.ToString(CultureInfo.InvariantCulture)}M");
			lines.Add("END:VALARM");
		}

		lines.Add("END:VEVENT");
		lines.Add("END:VCALENDAR");

		var builder = new StringBuilder();

		foreach (string line in lines)
		{
			builder.Append(Fold(line)).Append("\r\n");
		}

		return builder.ToString();
	}

	/// <summary>The same journey gives the same id: planned times, stops and lines (not the delays).</summary>
	public static string Uid(Journey journey)
	{
		var key = new StringBuilder();

		foreach (JourneyLeg leg in journey.Legs)
		{
			key.Append(leg.Mode).Append('|')
				.Append(leg.Line?.Name).Append('|')
				.Append(leg.From.Id).Append('|')
				.Append(leg.To.Id).Append('|')
				.Append((leg.ScheduledDeparture ?? leg.EffectiveDeparture)?.ToUnixTimeSeconds()).Append(';');
		}

		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()));

		return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
	}

	private static string Utc(DateTimeOffset value) =>
		value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

	/// <summary>Backslash, semicolon and comma are escaped, line breaks become \n.</summary>
	private static string Escape(string text) =>
		text.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace(";", "\;", StringComparison.Ordinal)
			.Replace(",", "\\,", StringComparison.Ordinal)
			.Replace("\r\n", "\\n", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal)
			.Replace("\r", "\\n", StringComparison.Ordinal);

	/// <summary>Content lines are at most 75 octets; a longer one continues on a line that starts with a space.</summary>
	private static string Fold(string line)
	{
		if (Encoding.UTF8.GetByteCount(line) <= 75)
		{
			return line;
		}

		var result = new StringBuilder();
		int octets = 0;

		foreach (Rune rune in line.EnumerateRunes())
		{
			int size = rune.Utf8SequenceLength;

			if (octets + size > 75)
			{
				result.Append("\r\n ");
				octets = 1;
			}

			result.Append(rune.ToString());
			octets += size;
		}

		return result.ToString();
	}
}
