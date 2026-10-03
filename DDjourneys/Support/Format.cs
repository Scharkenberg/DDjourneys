using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support;

/// <summary>
/// The single path for every user-visible time, duration,
/// delay and transport-mode string.
/// Timezone handling lives here and nowhere else.
/// </summary>
public static class Format
{
	private static readonly TimeZoneInfo Zone = ResolveZone();

	/// <summary>
	/// The current moment with the provider-zone offset (Europe/Berlin), to the second. The one clock
	/// for "is it active now", "is it today" and search times; never use the device's local zone.
	/// </summary>
	public static DateTimeOffset Now() =>
		TimeZoneInfo.ConvertTime(
			DateTimeOffset.UtcNow,
			Zone);

	/// <summary>Current wall-clock time in the provider zone (Europe/Berlin), floored to the minute.</summary>
	public static DateTime NowLocal()
	{
		DateTime n =
			TimeZoneInfo.ConvertTime(
				DateTimeOffset.UtcNow,
				Zone).DateTime;

		return new DateTime(
			n.Year,
			n.Month,
			n.Day,
			n.Hour,
			n.Minute,
			0,
			DateTimeKind.Unspecified);
	}

	/// <summary>
	/// Provider-zone wall-clock time of a moment.
	/// Used for relative day labels.
	/// </summary>
	public static DateTime ToWall(DateTimeOffset value) =>
		TimeZoneInfo.ConvertTime(value, Zone).DateTime;

	/// <summary>
	/// Converts a provider-zone wall-clock time to an offset time.
	/// Handles DST gaps and overlaps.
	/// </summary>
	public static DateTimeOffset ToOffset(DateTime wallClock)
	{
		DateTime local =
			DateTime.SpecifyKind(
				wallClock,
				DateTimeKind.Unspecified);

		if (Zone.IsInvalidTime(local))
		{
			local = local.AddHours(1);
		}

		TimeSpan offset =
			Zone.IsAmbiguousTime(local)
				? Zone.GetAmbiguousTimeOffsets(local).Max()
				: Zone.GetUtcOffset(local);

		return new DateTimeOffset(local, offset);
	}

	/// <summary>
	/// Localized "Today", "Tomorrow" or calendar date relative
	/// to the provider-zone today.
	/// </summary>
	public static string DayLabel(DateTime day)
	{
		int diff =
			(day.Date - NowLocal().Date).Days;

		IUiStrings strings =
			LocalizationService.Current.CurrentStrings;

		return diff switch
		{
			0 => strings.Common.Today,
			1 => strings.Common.Tomorrow,
			_ => day.ToString(
				"ddd, d MMM",
				CultureInfo.CurrentCulture)
		};
	}

	/// <summary>
	/// Returns the localized passenger-visible name of a transport mode.
	/// </summary>
	public static string TransportMode(TransitMode mode)
	{
		TransportStrings strings =
			LocalizationService.Current.CurrentStrings.Transport;

		return mode switch
		{
			TransitMode.Walk =>
				strings.Walk,

			TransitMode.Bus =>
				strings.Bus,

			TransitMode.Tram =>
				strings.Tram,

			TransitMode.Subway =>
				strings.Subway,

			TransitMode.SuburbanRail =>
				strings.SuburbanRail,

			TransitMode.RegionalTrain =>
				strings.RegionalTrain,

			TransitMode.LongDistanceTrain =>
				strings.LongDistanceTrain,

			TransitMode.Ferry =>
				strings.Ferry,

			TransitMode.CableCar =>
				strings.CableCar,

			TransitMode.Taxi =>
				strings.Taxi,

			TransitMode.OnDemand =>
				strings.OnDemand,

			_ =>
				LocalizationService.Current.CurrentStrings.Common.Unknown
		};
	}

	/// <summary>
	/// Null-safe time; "–" when the provider gave none.
	/// </summary>
	public static string TimeOrDash(DateTimeOffset? value) =>
		value is { } v && v != default
			? Time(v)
			: "\u2013";

	/// <summary>
	/// Null-safe duration between two moments; "–" when either is unknown.
	/// </summary>
	public static string Duration(
		DateTimeOffset? start,
		DateTimeOffset? end) =>
		start is { } a
			&& end is { } b
			&& a != default
			&& b != default
			? Duration(b - a)
			: "\u2013";

	public static string Time(DateTimeOffset value) =>
		TimeZoneInfo.ConvertTime(value, Zone)
			.ToString(
				"HH:mm",
				CultureInfo.CurrentCulture);

	public static string Duration(TimeSpan value)
	{
		int minutes =
			Math.Max(
				0,
				(int)Math.Round(value.TotalMinutes));

		return minutes >= 60
			? $"{minutes / 60} h {minutes % 60:00} min"
			: $"{minutes} min";
	}

	/// <summary>
	/// "+3 min" for late, "−1 min" for early,
	/// null when on time or unknown.
	/// </summary>
	public static string? Delay(TimeSpan? value)
	{
		if (value is not { } delay)
		{
			return null;
		}

		int minutes =
			(int)Math.Round(delay.TotalMinutes);

		return minutes switch
		{
			0 => null,
			> 0 => $"+{minutes} min",
			_ => $"\u2212{-minutes} min"
		};
	}

	private static TimeZoneInfo ResolveZone()
	{
		foreach (string id in
			new[]
			{
				"Europe/Berlin",
				"W. Europe Standard Time"
			})
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