using DDjourneys.Core.Models;
using DDjourneys.Localization;

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
	public string? PlaceText { get; init; }
	public string? PlatformText { get; init; }
	public required Color NodeColor { get; init; }

	public bool HasDelay => DelayText is not null;
	public bool HasPlace => PlaceText is not null;
	public bool HasPlatform => PlatformText is not null;
}

/// <summary>A stop between departure and arrival, shown when a leg is expanded.</summary>
public sealed class IntermediateRow : TimelineRow
{
	public required string Time { get; init; }
	public required string Name { get; init; }
	public string? PlaceText { get; init; }
	public string? DelayText { get; init; }
	public bool IsNotServed { get; init; }

	public bool HasDelay => DelayText is not null;
	public bool HasPlace => PlaceText is not null;
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
		IsExpanded
			? LocalizationService.Current.CurrentStrings.Journey.HideStops
			: Intermediates.Count == 1
				? LocalizationService.Current.CurrentStrings.Journey.OneStop
				: string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					LocalizationService.Current.CurrentStrings.Journey.MultipleStops,
					Intermediates.Count);

	public string Chevron =>
		IsExpanded ? "\u25B4" : "\u25BE";
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
	public string? PlaceText { get; init; }
	public bool HasPlace => PlaceText is not null;
	public string? ContinuesFrom { get; init; }
	public string? PlatformText { get; init; }
	public string? WaitText { get; init; }
	public string? WalkText { get; init; }
	public string? RiskText { get; init; }
	public bool HasWait => WaitText is not null;
	public bool HasWalk => WalkText is not null;
	public bool HasRisk => RiskText is not null;
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
	public bool Expanded { get; init; }
	public bool Technical { get; init; }
}

/// <summary>What the timeline shows; comes from the user's settings.</summary>
public sealed record TimelineOptions(
	bool ShowWalking = true,
	bool ExpandNotices = false,
	bool Technical = false);

/// <summary>
/// Turns TimelineBuilder items into display rows. Two rides joined by a boundary
/// become one interchange row instead of an arrival row plus a departure row.
/// </summary>
public static class TimelineRowFactory
{
	private const string Dash = "\u2013";

	public static IReadOnlyList<TimelineRow> Build(
		Journey journey,
		TimelineOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(journey);

		options ??= new TimelineOptions();

		IReadOnlyList<TimelineItem> items =
			TimelineBuilder.Build(journey);

		var rows = new List<TimelineRow>();

		if (items.Count > 0 && items[0] is WalkItem firstWalk)
		{
			rows.Add(
				EndpointStop(
					firstWalk.Leg.From.Name,
					PlaceOf(firstWalk.Leg.From),
					firstWalk.Leg.EffectiveDeparture,
					Colors.Transparent,
					WalkColor()));
		}

		for (int i = 0; i < items.Count; i++)
		{
			switch (items[i])
			{
				case RideItem ride:
					AddRide(rows, items, i, ride.Leg, options);
					break;

				case WalkItem walk:
					if (options.ShowWalking)
					{
						string walkTime =
							Format.Duration(
								walk.Leg.EffectiveDeparture,
								walk.Leg.EffectiveArrival);

						JourneyStrings strings =
							LocalizationService.Current.CurrentStrings.Journey;

						rows.Add(
							new WalkRow
							{
								Text =
									$"{strings.Walk} {walkTime}",
								Caption =
									$"{strings.To} {walk.Leg.To.Name}",
								RailBottom = WalkColor(),
								Description =
									$"{strings.Walk} {walkTime} {strings.To} {walk.Leg.To.Name}"
							});
					}

					break;

				case BoundaryItem boundary:
					AddBoundary(
						rows,
						items,
						i,
						boundary,
						options);
					break;
			}
		}

		if (items.Count > 0 && items[^1] is WalkItem lastWalk)
		{
			rows.Add(
				EndpointStop(
					lastWalk.Leg.To.Name,
					PlaceOf(lastWalk.Leg.To),
					lastWalk.Leg.EffectiveArrival,
					WalkColor(),
					Colors.Transparent));
		}

		for (int i = 0; i < rows.Count; i++)
		{
			rows[i].Index = i;
		}

		return rows;
	}

	private static void AddRide(
		List<TimelineRow> rows,
		IReadOnlyList<TimelineItem> items,
		int i,
		JourneyLeg leg,
		TimelineOptions options)
	{
		Color color = ModeColors.For(leg.Mode);
		JourneyStrings strings =
			LocalizationService.Current.CurrentStrings.Journey;

		bool departureMerged =
			i >= 2
			&& items[i - 1] is BoundaryItem
			&& items[i - 2] is RideItem;

		bool arrivalMerged =
			i + 2 < items.Count
			&& items[i + 1] is BoundaryItem
			&& items[i + 2] is RideItem;

		if (!departureMerged)
		{
			string? delay =
				Format.Delay(leg.DepartureDelay);

			rows.Add(
				new StopRow
				{
					Time =
						Format.TimeOrDash(
							leg.EffectiveDeparture),
					ScheduledTime =
						delay is null
							? null
							: Format.TimeOrDash(
								leg.ScheduledDeparture),
					DelayText = delay,
					Name = leg.From.Name,
					PlaceText = PlaceOf(leg.From),
					PlatformText =
						PlatformText(
							leg.DeparturePlatform),
					NodeColor = color,
					RailTop =
						RailAt(items, i - 1, -1),
					RailBottom = color,
					Description =
						$"{Format.TimeOrDash(leg.EffectiveDeparture)}, " +
						$"{strings.Depart} {leg.From.Name}"
				});
		}

		DateTimeOffset now = DateTimeOffset.Now;

		rows.Add(
			new LegRow
			{
				LineText =
					leg.Line?.Name
					?? Format.TransportMode(leg.Mode),
				ModeColor = color,
				Direction =
					string.IsNullOrWhiteSpace(
						leg.Line?.Destination)
						? null
						: $"{strings.To} {leg.Line!.Destination}",
				DurationText =
					Format.Duration(
						leg.EffectiveDeparture,
						leg.EffectiveArrival),
				FeaturesText =
					Features(leg.Vehicle?.Accessibility),
				IsCancelled = leg.IsCancelled,
				IsActive =
					!leg.IsCancelled
					&& leg.EffectiveDeparture is { } start
					&& leg.EffectiveArrival is { } end
					&& now >= start
					&& now <= end,
				Intermediates =
					leg.Stops
						.Skip(1)
						.Take(
							Math.Max(
								0,
								leg.Stops.Count - 2))
						.Select(
							stop =>
								new IntermediateRow
								{
									Time =
										TimeOf(
											stop.EffectiveDeparture
											?? stop.EffectiveArrival),
									Name =
										stop.Station.Name,
									PlaceText =
										PlaceOf(stop.Station),
									DelayText =
										Format.Delay(
											stop.DepartureDelay
											?? stop.ArrivalDelay),
									IsNotServed =
										stop.IsCancelled,
									RailBottom = color,
									Description =
										$"{TimeOf(stop.EffectiveDeparture ?? stop.EffectiveArrival)}, " +
										$"{stop.Station.Name}"
								})
						.ToList(),
				RailBottom = color,
				Description =
					$"{leg.Line?.Name ?? Format.TransportMode(leg.Mode)}, " +
					Format.Duration(
						leg.EffectiveDeparture,
						leg.EffectiveArrival)
			});

		foreach (string notice in leg.Notices.Distinct())
		{
			rows.Add(
				new NoticeRow
				{
					Text = notice,
					RailBottom = color,
					Description = notice,
					Expanded = options.ExpandNotices,
					Technical = options.Technical
				});
		}

		if (!arrivalMerged)
		{
			string? delay =
				Format.Delay(leg.ArrivalDelay);

			rows.Add(
				new StopRow
				{
					Time =
						Format.TimeOrDash(
							leg.EffectiveArrival),
					ScheduledTime =
						delay is null
							? null
							: Format.TimeOrDash(
								leg.ScheduledArrival),
					DelayText = delay,
					Name = leg.To.Name,
					PlaceText = PlaceOf(leg.To),
					PlatformText =
						PlatformText(
							leg.ArrivalPlatform),
					NodeColor = color,
					RailTop = color,
					RailBottom =
						RailAt(items, i + 1, +1),
					Description =
						$"{Format.TimeOrDash(leg.EffectiveArrival)}, " +
						$"{strings.Arrive} {leg.To.Name}"
				});
		}
	}

	private static void AddBoundary(
		List<TimelineRow> rows,
		IReadOnlyList<TimelineItem> items,
		int i,
		BoundaryItem boundary,
		TimelineOptions options)
	{
		bool between =
			i > 0
			&& i + 1 < items.Count
			&& items[i - 1] is RideItem
			&& items[i + 1] is RideItem;

		Color next =
			RailAt(items, i + 1, +1);

		JourneyStrings strings =
			LocalizationService.Current.CurrentStrings.Journey;

		if (between)
		{
			JourneyLeg from =
				((RideItem)items[i - 1]).Leg;

			JourneyLeg to =
				((RideItem)items[i + 1]).Leg;

			rows.Add(
				new InterchangeRow
				{
					ArrivalTime =
						Format.TimeOrDash(
							from.EffectiveArrival),
					ArrivalDelay =
						Format.Delay(from.ArrivalDelay),
					DepartureTime =
						Format.TimeOrDash(
							to.EffectiveDeparture),
					DepartureDelay =
						Format.Delay(to.DepartureDelay),
					Name = from.To.Name,
					PlaceText =
						PlaceOf(from.To),
					ContinuesFrom =
						from.To.Name == to.From.Name
							? null
							: $"{strings.ContinueFrom} {to.From.Name}",
					PlatformText =
						PlatformPair(
							from.ArrivalPlatform,
							to.DeparturePlatform),
					WaitText =
						boundary.ShowWait
							? boundary.Wait < TimeSpan.FromMinutes(1)
								? strings.ImmediateChange
								: $"{Format.Duration(boundary.Wait)} {strings.ToChange}"
							: null,
					WalkText =
						boundary.WalkTime is { } walk
							? $"{strings.WalkAbout} {Format.Duration(walk)} {strings.BetweenStops}"
							: null,
					RiskText =
						boundary.Endangered
							? strings.ConnectionMayBeMissed
							: null,
					NodeColor =
						ModeColors.For(to.Mode),
					RailTop =
						ModeColors.For(from.Mode),
					RailBottom =
						ModeColors.For(to.Mode),
					Description =
						$"{strings.ChangeAt} {from.To.Name}, " +
						Format.Duration(boundary.Wait)
				});
		}

		foreach (string note in boundary.Notes)
		{
			rows.Add(
				new NoticeRow
				{
					Text = note,
					RailBottom = next,
					Description = note,
					Expanded = options.ExpandNotices,
					Technical = options.Technical
				});
		}
	}

	private static StopRow EndpointStop(
		string name,
		string? place,
		DateTimeOffset? time,
		Color top,
		Color bottom) =>
		new()
		{
			PlaceText = place,
			Time = Format.TimeOrDash(time),
			Name = name,
			NodeColor = WalkColor(),
			RailTop = top,
			RailBottom = bottom,
			Description =
				$"{Format.TimeOrDash(time)}, {name}"
		};

	/// <summary>
	/// Colour of the neighbouring leg in the given direction,
	/// skipping boundaries.
	/// </summary>
	private static Color RailAt(
		IReadOnlyList<TimelineItem> items,
		int index,
		int step)
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

	/// <summary>
	/// The city/village/region the provider delivers with a stop;
	/// null when absent or already in the name.
	/// </summary>
	private static string? PlaceOf(Station station) =>
		string.IsNullOrWhiteSpace(station.Place)
			|| station.Name.Contains(
				station.Place,
				StringComparison.OrdinalIgnoreCase)
			? null
			: station.Place.Trim();

	private static Color WalkColor() =>
		ModeColors
			.For(TransitMode.Walk)
			.WithAlpha(0.5f);

	private static string TimeOf(DateTimeOffset? time) =>
		time is { } value
			? Format.Time(value)
			: Dash;

	private static string? PlatformText(string? platform)
	{
		if (string.IsNullOrWhiteSpace(platform))
		{
			return null;
		}

		JourneyStrings strings =
			LocalizationService.Current.CurrentStrings.Journey;

		// Short values ("3", "A") read as a platform number;
		// longer ones are shown as given.
		return platform.Length <= 3
			? $"{strings.Platform} {platform}"
			: platform;
	}

	private static string? PlatformPair(
		string? arrival,
		string? departure)
	{
		if (string.IsNullOrWhiteSpace(arrival)
			&& string.IsNullOrWhiteSpace(departure))
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(arrival))
		{
			return PlatformText(departure);
		}

		if (string.IsNullOrWhiteSpace(departure))
		{
			return PlatformText(arrival);
		}

		if (string.Equals(
				arrival,
				departure,
				StringComparison.OrdinalIgnoreCase))
		{
			return PlatformText(arrival);
		}

		return $"{PlatformText(arrival)} \u2192 {PlatformText(departure)}";
	}

	private static string? Features(
		AccessibilityInfo? info)
	{
		if (info is null)
		{
			return null;
		}

		JourneyStrings strings =
			LocalizationService.Current.CurrentStrings.Journey;

		var values = new List<string>();

		if (info.LowFloor == true)
		{
			values.Add(strings.LowFloor);
		}

		if (info.WheelchairAccessible == true)
		{
			values.Add(strings.WheelchairAccessible);
		}

		if (info.BicycleAccessible == true)
		{
			values.Add(strings.BicycleAccessible);
		}

		return values.Count == 0
			? null
			: string.Join(" · ", values);
	}
}