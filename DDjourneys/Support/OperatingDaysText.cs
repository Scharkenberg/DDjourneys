using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support;

/// <summary>Words for the days a service runs and for the passengers a ticket is for.</summary>
public static class OperatingDaysText
{
	/// <summary>
	/// "Runs Mon–Fri" from the pattern, or the provider's own wording. Null when the service runs every day
	/// (nothing to say) or the provider says nothing.
	/// </summary>
	public static string? Describe(OperatingDays? days, ExtrasStrings strings, CultureInfo? culture = null)
	{
		ArgumentNullException.ThrowIfNull(strings);

		if (days is null)
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(days.Description))
		{
			return days.Description.Trim();
		}

		IReadOnlyList<DayOfWeek> weekdays = days.Weekdays;

		return weekdays.Count is 0 or 7
			? null
			: string.Format(culture ?? CultureInfo.CurrentCulture, strings.RunsOn, Weekdays(weekdays, culture ?? CultureInfo.CurrentCulture));
	}

	/// <summary>"Mon–Fri, Sun": runs of three or more consecutive days are joined, the rest are listed.</summary>
	public static string Weekdays(IReadOnlyList<DayOfWeek> days, CultureInfo culture)
	{
		ArgumentNullException.ThrowIfNull(days);
		ArgumentNullException.ThrowIfNull(culture);

		static int Index(DayOfWeek day) =>
			day == DayOfWeek.Sunday ? 7 : (int)day;

		int[] ordered = [.. days.Select(Index).Distinct().Order()];

		string Name(int index) =>
			culture.DateTimeFormat.AbbreviatedDayNames[index % 7].TrimEnd('.');

		var parts = new List<string>();

		for (int start = 0; start < ordered.Length;)
		{
			int end = start;

			while (end + 1 < ordered.Length && ordered[end + 1] == ordered[end] + 1)
			{
				end++;
			}

			if (end - start >= 2)
			{
				parts.Add($"{Name(ordered[start])}–{Name(ordered[end])}");
			}
			else
			{
				for (int index = start; index <= end; index++)
				{
					parts.Add(Name(ordered[index]));
				}
			}

			start = end + 1;
		}

		return string.Join(", ", parts);
	}

	public static string Passenger(PassengerCategory category, ExtrasStrings strings) =>
		category switch
		{
			PassengerCategory.Youth => strings.PassengerYouth,
			PassengerCategory.Child => strings.PassengerChild,
			PassengerCategory.Senior => strings.PassengerSenior,
			PassengerCategory.Disabled => strings.PassengerDisabled,
			_ => strings.PassengerAdult
		};
}
