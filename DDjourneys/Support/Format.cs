using System.Globalization;

namespace DDjourneys.Support;

/// <summary>
/// The single path for every user-visible time and duration string.
/// Timezone handling lives here and nowhere else.
/// </summary>
public static class Format
{
	private static readonly TimeZoneInfo Zone = ResolveZone();

	public static string Time(DateTimeOffset value) =>
		TimeZoneInfo.ConvertTime(value, Zone)
			.ToString("HH:mm", CultureInfo.InvariantCulture);

	public static string Duration(TimeSpan value)
	{
		int minutes = Math.Max(0, (int)Math.Round(value.TotalMinutes));

		return minutes >= 60
			? $"{minutes / 60} h {minutes % 60:00} min"
			: $"{minutes} min";
	}

	/// <summary>
	/// "+3 min" for late, "−1 min" for early, null when on time or unknown.
	/// </summary>
	public static string? Delay(TimeSpan? value)
	{
		if (value is not { } delay)
		{
			return null;
		}

		int minutes = (int)Math.Round(delay.TotalMinutes);

		return minutes switch
		{
			0 => null,
			> 0 => $"+{minutes} min",
			_ => $"\u2212{-minutes} min"
		};
	}

	private static TimeZoneInfo ResolveZone()
	{
		foreach (string id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (TimeZoneNotFoundException)
			{
			}
			catch (InvalidTimeZoneException)
			{
			}
		}

		return TimeZoneInfo.Local;
	}
}
