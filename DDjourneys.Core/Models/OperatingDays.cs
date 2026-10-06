namespace DDjourneys.Core.Models;

/// <summary>
/// The days a service runs: the provider's own words, and (TRIAS) a bit pattern with one character per
/// day from <see cref="From"/> on, "1" for a day it runs.
/// </summary>
public sealed record OperatingDays
{
	public DateOnly? From { get; init; }

	public DateOnly? To { get; init; }

	public string Pattern { get; init; } = string.Empty;

	/// <summary>The provider's wording ("Mo-Fr, not on public holidays"), when it gives one.</summary>
	public string? Description { get; init; }

	public bool HasContent =>
		!string.IsNullOrWhiteSpace(Description)
		|| Weekdays.Count > 0;

	/// <summary>The weekdays the pattern runs on, Monday first. Empty without a usable pattern.</summary>
	public IReadOnlyList<DayOfWeek> Weekdays
	{
		get
		{
			if (From is not { } start || Pattern.Length == 0)
			{
				return [];
			}

			var days = new HashSet<DayOfWeek>();

			for (int index = 0; index < Pattern.Length && days.Count < 7; index++)
			{
				if (Pattern[index] == '1')
				{
					days.Add(start.AddDays(index).DayOfWeek);
				}
			}

			return
				[.. days.OrderBy(day => day == DayOfWeek.Sunday ? 7 : (int)day)];
		}
	}

	/// <summary>True when the pattern says the service runs on <paramref name="day"/>; null when it cannot say.</summary>
	public bool? RunsOn(DateOnly day)
	{
		if (From is not { } start || Pattern.Length == 0)
		{
			return null;
		}

		int index = day.DayNumber - start.DayNumber;

		return index >= 0 && index < Pattern.Length
			? Pattern[index] == '1'
			: null;
	}
}
