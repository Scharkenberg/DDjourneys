using DDjourneys.Core.Models;

namespace DDjourneys.Support;

/// <summary>
/// One row of the journey timeline. Rails are drawn per row: RailTop is the
/// line above a stop's node, RailBottom the line below it (and the full-height
/// line for rows that have no node).
/// </summary>
public abstract class TimelineRow : ObservableObject
{
	public Color RailTop { get; init; } = Colors.Transparent;
	public Color RailBottom { get; init; } = Colors.Transparent;
	public string Description { get; init; } = string.Empty;

	/// <summary>Position used to stagger the entrance animation.</summary>
	public int Index { get; set; }
}

/// <summary>A stop where a leg starts or ends (or the journey's walking start/end).</summary>
public sealed class StopRow : TimelineRow
{
	public required string Time { get; init; }
	public string? ScheduledTime { get; init; }
	public string? DelayText { get; init; }
	public required string Name { get; init; }
	public string? PlatformText { get; init; }
	public required Color NodeColor { get; init; }

	public bool HasDelay => DelayText is not null;
	public bool HasPlatform => PlatformText is not null;
}

/// <summary>A stop between departure and arrival, shown when a leg is expanded.</summary>
public sealed class IntermediateRow : TimelineRow
{
	public required string Time { get; init; }
	public required string Name { get; init; }
	public string? DelayText { get; init; }
	public bool IsNotServed { get; init; }

	public bool HasDelay => DelayText is not null;
}

/// <summary>Line, direction and duration of a ride, with the expand toggle.</summary>
public sealed class LegRow : TimelineRow
{
	public required string LineText { get; init; }
	public required Color ModeColor { get; init; }
	public string? Direction { get; init; }
	public required string DurationText { get; init; }
	public string? FeaturesText { get; init; }
	public bool IsCancelled { get; init; }
	public bool IsActive { get; init; }
	public IReadOnlyList<IntermediateRow> Intermediates { get; init; } = [];

	public bool HasDirection => Direction is not null;
	public bool HasFeatures => FeaturesText is not null;
	public bool HasIntermediates => Intermediates.Count > 0;

	public bool IsExpanded
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(StopsText));
				OnPropertyChanged(nameof(Chevron));
			}
		}
	}

	public string StopsText =>
		IsExpanded ? "Hide stops"
		: Intermediates.Count == 1 ? "1 stop"
		: $"{Intermediates.Count} stops";

	public string Chevron => IsExpanded ? "\u25B4" : "\u25BE";
}

public sealed class WalkRow : TimelineRow
{
	public required string Text { get; init; }
	public string? Caption { get; init; }

	public bool HasCaption => Caption is not null;
}

/// <summary>Arrival of one ride and departure of the next, at the same place.</summary>
public sealed class InterchangeRow : TimelineRow
{
	public required string ArrivalTime { get; init; }
	public string? ArrivalDelay { get; init; }
	public required string DepartureTime { get; init; }
	public string? DepartureDelay { get; init; }
	public required string Name { get; init; }
	public string? ContinuesFrom { get; init; }
	public string? PlatformText { get; init; }
	public string? WaitText { get; init; }
	public bool HasWait => WaitText is not null;
	public required Color NodeColor { get; init; }

	public bool HasArrivalDelay => ArrivalDelay is not null;
	public bool HasDepartureDelay => DepartureDelay is not null;
	public bool HasContinuesFrom => ContinuesFrom is not null;
	public bool HasPlatform => PlatformText is not null;
}

/// <summary>A message from the provider (leg notice, or a note on a boundary).</summary>
public sealed class NoticeRow : TimelineRow
{
	public required string Text { get; init; }
}

/// <summary>
/// Turns TimelineBuilder items into display rows. Two rides joined by a boundary
/// become one interchange row instead of an arrival row plus a departure row.
/// </summary>
public static class TimelineRowFactory
{
	private const string Dash = "\u2013";

	public static IReadOnlyList<TimelineRow> Build(Journey journey)
	{
		IReadOnlyList<TimelineItem> items = TimelineBuilder.Build(journey);
		var rows = new List<TimelineRow>();

		if (items.Count > 0 && items[0] is WalkItem firstWalk)
		{
			rows.Add(EndpointStop(
				firstWalk.Leg.From.Name,
				firstWalk.Leg.EffectiveDeparture ?? default,
				top: Colors.Transparent,
				bottom: WalkColor()));
		}

		for (int i = 0; i < items.Count; i++)
		{
			switch (items[i])
			{
				case RideItem ride:
					AddRide(rows, items, i, ride.Leg);
					break;

				case WalkItem walk:
					rows.Add(new WalkRow
					{
						Text = $"Walk {Format.Duration((walk.Leg.EffectiveArrival ?? default) - (walk.Leg.EffectiveDeparture ?? default))}",
						Caption = $"to {walk.Leg.To.Name}",
						RailBottom = WalkColor(),
						Description = $"Walk {Format.Duration((walk.Leg.EffectiveArrival ?? default) - (walk.Leg.EffectiveDeparture ?? default))} to {walk.Leg.To.Name}"
					});
					break;

				case BoundaryItem boundary:
					AddBoundary(rows, items, i, boundary);
					break;
			}
		}

		if (items.Count > 0 && items[^1] is WalkItem lastWalk)
		{
			rows.Add(EndpointStop(
				lastWalk.Leg.To.Name,
				lastWalk.Leg.EffectiveArrival ?? default,
				top: WalkColor(),
				bottom: Colors.Transparent));
		}

		for (int i = 0; i < rows.Count; i++)
		{
			rows[i].Index = i;
		}

		return rows;
	}

	private static void AddRide(List<TimelineRow> rows, IReadOnlyList<TimelineItem> items, int i, JourneyLeg leg)
	{
		Color color = ModeColors.For(leg.Mode);

		bool departureMerged = i >= 2 && items[i - 1] is BoundaryItem && items[i - 2] is RideItem;
		bool arrivalMerged = i + 2 < items.Count && items[i + 1] is BoundaryItem && items[i + 2] is RideItem;

		if (!departureMerged)
		{
			string? delay = Format.Delay(leg.DepartureDelay);

			rows.Add(new StopRow
			{
				Time = Format.Time(leg.EffectiveDeparture ?? default),
				ScheduledTime = delay is null ? null : Format.Time(leg.ScheduledDeparture ?? default),
				DelayText = delay,
				Name = leg.From.Name,
				PlatformText = PlatformText(leg.DeparturePlatform),
				NodeColor = color,
				RailTop = RailAt(items, i - 1, -1),
				RailBottom = color,
				Description = $"{Format.Time(leg.EffectiveDeparture ?? default)}, depart {leg.From.Name}"
			});
		}

		DateTimeOffset now = DateTimeOffset.Now;

		rows.Add(new LegRow
		{
			LineText = leg.Line?.Name ?? leg.Mode.ToString(),
			ModeColor = color,
			Direction = string.IsNullOrWhiteSpace(leg.Line?.Destination) ? null : $"to {leg.Line!.Destination}",
			DurationText = Format.Duration((leg.EffectiveArrival ?? default) - (leg.EffectiveDeparture ?? default)),
			FeaturesText = Features(leg.Vehicle?.Accessibility),
			IsCancelled = leg.IsCancelled,
			IsActive = !leg.IsCancelled && now >= leg.EffectiveDeparture && now <= leg.EffectiveArrival,
			Intermediates = leg.Stops
				.Skip(1)
				.Take(Math.Max(0, leg.Stops.Count - 2))
				.Select(stop => new IntermediateRow
				{
					Time = TimeOf(stop.EffectiveDeparture ?? stop.EffectiveArrival),
					Name = stop.Station.Name,
					DelayText = Format.Delay(stop.DepartureDelay ?? stop.ArrivalDelay),
					IsNotServed = stop.IsCancelled,
					RailBottom = color,
					Description = $"{TimeOf(stop.EffectiveDeparture ?? stop.EffectiveArrival)}, {stop.Station.Name}"
				})
				.ToList(),
			RailBottom = color,
			Description = $"{leg.Line?.Name ?? leg.Mode.ToString()}, {Format.Duration((leg.EffectiveArrival ?? default) - (leg.EffectiveDeparture ?? default))}"
		});

		foreach (string notice in leg.Notices.Distinct())
		{
			rows.Add(new NoticeRow { Text = notice, RailBottom = color, Description = notice });
		}

		if (!arrivalMerged)
		{
			string? delay = Format.Delay(leg.ArrivalDelay);

			rows.Add(new StopRow
			{
				Time = Format.Time(leg.EffectiveArrival ?? default),
				ScheduledTime = delay is null ? null : Format.Time(leg.ScheduledArrival ?? default),
				DelayText = delay,
				Name = leg.To.Name,
				PlatformText = PlatformText(leg.ArrivalPlatform),
				NodeColor = color,
				RailTop = color,
				RailBottom = RailAt(items, i + 1, +1),
				Description = $"{Format.Time(leg.EffectiveArrival ?? default)}, arrive {leg.To.Name}"
			});
		}
	}

	private static void AddBoundary(List<TimelineRow> rows, IReadOnlyList<TimelineItem> items, int i, BoundaryItem boundary)
	{
		bool between = i > 0 && i + 1 < items.Count
			&& items[i - 1] is RideItem
			&& items[i + 1] is RideItem;

		Color next = RailAt(items, i + 1, +1);

		if (between)
		{
			JourneyLeg from = ((RideItem)items[i - 1]).Leg;
			JourneyLeg to = ((RideItem)items[i + 1]).Leg;

			rows.Add(new InterchangeRow
			{
				ArrivalTime = Format.Time(from.EffectiveArrival ?? default),
				ArrivalDelay = Format.Delay(from.ArrivalDelay),
				DepartureTime = Format.Time(to.EffectiveDeparture ?? default),
				DepartureDelay = Format.Delay(to.DepartureDelay),
				Name = from.To.Name,
				ContinuesFrom = from.To.Name == to.From.Name ? null : $"continue from {to.From.Name}",
				PlatformText = PlatformPair(from.ArrivalPlatform, to.DeparturePlatform),
				WaitText = boundary.ShowWait ? $"{Format.Duration(boundary.Wait)} to change" : null,
				NodeColor = ModeColors.For(to.Mode),
				RailTop = ModeColors.For(from.Mode),
				RailBottom = ModeColors.For(to.Mode),
				Description = $"Change at {from.To.Name}, {Format.Duration(boundary.Wait)}"
			});
		}

		foreach (string note in boundary.Notes)
		{
			rows.Add(new NoticeRow { Text = note, RailBottom = next, Description = note });
		}
	}

	private static StopRow EndpointStop(string name, DateTimeOffset time, Color top, Color bottom) => new()
	{
		Time = Format.Time(time),
		Name = name,
		NodeColor = WalkColor(),
		RailTop = top,
		RailBottom = bottom,
		Description = $"{Format.Time(time)}, {name}"
	};

	/// <summary>Colour of the neighbouring leg in the given direction, skipping boundaries.</summary>
	private static Color RailAt(IReadOnlyList<TimelineItem> items, int index, int step)
	{
		while (index >= 0 && index < items.Count)
		{
			switch (items[index])
			{
				case RideItem ride:
					return ModeColors.For(ride.Leg.Mode);
				case WalkItem:
					return WalkColor();
			}

			index += step;
		}

		return Colors.Transparent;
	}

	private static Color WalkColor() => ModeColors.For(TransitMode.Walk).WithAlpha(0.5f);

	private static string TimeOf(DateTimeOffset? time) => time is { } value ? Format.Time(value) : Dash;

	private static string? PlatformText(string? platform)
	{
		if (string.IsNullOrWhiteSpace(platform))
		{
			return null;
		}

		// Short values ("3", "A") read as a platform number; longer ones are shown as given.
		return platform.Length <= 3 ? $"Platform {platform}" : platform;
	}

	private static string? PlatformPair(string? arrival, string? departure)
	{
		if (string.IsNullOrWhiteSpace(arrival) || string.IsNullOrWhiteSpace(departure) || arrival == departure)
		{
			return PlatformText(string.IsNullOrWhiteSpace(arrival) ? departure : arrival);
		}

		return arrival.Length <= 3 && departure.Length <= 3
			? $"Platform {arrival} \u2192 {departure}"
			: $"{arrival} \u2192 {departure}";
	}

	private static string? Features(AccessibilityInfo? info)
	{
		if (info is null)
		{
			return null;
		}

		var features = new List<string>();

		if (info.LowFloor == true)
		{
			features.Add("Low floor");
		}

		if (info.WheelchairAccessible == true)
		{
			features.Add("Wheelchair accessible");
		}

		if (info.BicycleAccessible == true)
		{
			features.Add("Bicycle accessible");
		}

		return features.Count == 0 ? null : string.Join(" \u00B7 ", features);
	}
}
