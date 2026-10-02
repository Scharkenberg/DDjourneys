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
	public Color RailTop { get; init; } =
		Colors.Transparent;


	public Color RailBottom { get; init; } =
		Colors.Transparent;


	public string Description { get; init; } =
		string.Empty;


	/// <summary>
	/// Position used to stagger the entrance animation.
	/// </summary>
	public int Index { get; set; }
}


/// <summary>
/// A stop where a leg starts or ends (or the journey's walking start/end).
/// </summary>
public sealed class StopRow : TimelineRow
{
	public required string Time { get; init; }

	public string? ScheduledTime { get; init; }

	public string? DelayText { get; init; }

	public required string Name { get; init; }

	/// <summary>City/region shown right behind the name; null when none applies.</summary>
	public string? Place { get; init; }

	public string? PlatformText { get; init; }

	public required Color NodeColor { get; init; }

	/// <summary>Occupancy at this stop; Unknown hides the indicator.</summary>
	public OccupancyLevel Occupancy { get; init; } =
		OccupancyLevel.Unknown;

	/// <summary>The vehicle is ahead of the timetable: the difference is good news, not a delay.</summary>
	public bool IsEarly { get; init; }

	public bool HasDelay =>
		DelayText is not null;

	public bool HasPlatform =>
		PlatformText is not null;

	public bool HasOccupancy =>
		Occupancy != OccupancyLevel.Unknown;
}


/// <summary>
/// A stop between departure and arrival, shown when a leg is expanded.
/// </summary>
public sealed class IntermediateRow : TimelineRow
{
	public required string Time { get; init; }

	public required string Name { get; init; }

	public string? Place { get; init; }

	public string? DelayText { get; init; }

	public bool IsEarly { get; init; }

	public bool IsNotServed { get; init; }

	public OccupancyLevel Occupancy { get; init; } =
		OccupancyLevel.Unknown;

	public bool HasDelay =>
		DelayText is not null;

	public bool HasOccupancy =>
		Occupancy != OccupancyLevel.Unknown;
}


/// <summary>
/// Line, direction and duration of a ride, with the expand toggle.
/// </summary>
public sealed class LegRow : TimelineRow
{
	public required string LineText { get; init; }

	public required Color ModeColor { get; init; }

	public required ChipLook Look { get; init; }

	public string? Direction { get; init; }

	public required string DurationText { get; init; }

	public string? FeaturesText { get; init; }

	/// <summary>Occupancy of the vehicle on this ride.</summary>
	public OccupancyLevel Occupancy { get; init; } =
		OccupancyLevel.Unknown;

	public string? OccupancyText { get; init; }

	public bool IsCancelled { get; init; }

	public bool IsActive { get; init; }

	public IReadOnlyList<IntermediateRow> Intermediates { get; init; } =
		[];


	public bool HasDirection =>
		Direction is not null;

	public bool HasFeatures =>
		FeaturesText is not null;

	public bool HasOccupancy =>
		Occupancy != OccupancyLevel.Unknown;

	public bool HasIntermediates =>
		Intermediates.Count > 0;


	public bool IsExpanded
	{
		get => field;
		set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(StopsText));

				OnPropertyChanged(
					nameof(ChevronRotation));
			}
		}
	}


	public string StopsText =>
		IsExpanded
			? LocalizationService.Current
				.CurrentStrings
				.Journey
				.HideStops
			: Intermediates.Count == 1
				? LocalizationService.Current
					.CurrentStrings
					.Journey
					.OneStop
				: string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					LocalizationService.Current
						.CurrentStrings
						.Journey
						.MultipleStops,
					Intermediates.Count);


	/// <summary>Filled triangle: pointing down when collapsed, up when expanded.</summary>
	public double ChevronRotation =>
		IsExpanded
			? 180
			: 0;
}


public sealed class WalkRow : TimelineRow
{
	public required string Text { get; init; }

	/// <summary>Muted lead-in of the caption ("to", or "within the stop").</summary>
	public string? CaptionPrefix { get; init; }

	/// <summary>Stop the walk leads to; empty when the walk stays inside one stop.</summary>
	public string CaptionName { get; init; } =
		string.Empty;

	/// <summary>City of that stop, shown under the name.</summary>
	public string? CaptionPlace { get; init; }

	public bool HasCaption =>
		CaptionPrefix is not null
		|| CaptionName.Length > 0;
}


/// <summary>
/// Arrival of one ride and departure of the next, at the same place.
/// </summary>
public sealed class InterchangeRow : TimelineRow
{
	public required string ArrivalTime { get; init; }

	public string? ArrivalDelay { get; init; }

	public required string DepartureTime { get; init; }

	public string? DepartureDelay { get; init; }

	public required string Name { get; init; }

	public string? Place { get; init; }

	/// <summary>Occupancy on arrival (end of the incoming ride).</summary>
	public OccupancyLevel ArrivalOccupancy { get; init; } =
		OccupancyLevel.Unknown;

	/// <summary>Occupancy on departure (start of the outgoing ride).</summary>
	public OccupancyLevel DepartureOccupancy { get; init; } =
		OccupancyLevel.Unknown;

	public string? ConnectionText { get; init; }

	public string? WaitText { get; init; }

	public string? WalkText { get; init; }

	public string? RiskText { get; init; }

	public bool HasArrivalOccupancy =>
		ArrivalOccupancy != OccupancyLevel.Unknown;

	public bool HasDepartureOccupancy =>
		DepartureOccupancy != OccupancyLevel.Unknown;

	public bool HasConnection =>
		ConnectionText is not null;

	public bool HasWait =>
		WaitText is not null;

	public bool HasWalk =>
		WalkText is not null;

	public bool HasRisk =>
		RiskText is not null;

	public required Color NodeColor { get; init; }

	public bool ArrivalIsEarly { get; init; }

	public bool DepartureIsEarly { get; init; }

	public bool HasArrivalDelay =>
		ArrivalDelay is not null;

	public bool HasDepartureDelay =>
		DepartureDelay is not null;
}


/// <summary>
/// A message from the provider (leg notice, or a note on a boundary).
/// </summary>
public sealed class NoticeRow : TimelineRow
{
	public required string Text { get; init; }

	public bool Expanded { get; init; }

	public bool Technical { get; init; }
}


/// <summary>
/// What the timeline shows; comes from the user's settings.
/// </summary>
public sealed record TimelineOptions(
	bool ShowWalking = true,
	bool ExpandNotices = false,
	bool Technical = false,
	bool ShowOccupancy = true,
	bool ShowPlatforms = true,
	bool ExpandStops = false);


/// <summary>
/// Turns TimelineBuilder items into display rows. Two rides joined by a boundary
/// become one interchange row instead of an arrival row plus a departure row.
/// </summary>
public static class TimelineRowFactory
{
	private const string Dash =
		"\u2013";


	public static IReadOnlyList<TimelineRow> Build(
		Journey journey,
		TimelineOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(journey);

		options ??=
			new TimelineOptions();


		IReadOnlyList<TimelineItem> items =
			TimelineBuilder.Build(journey);


		var rows =
			new List<TimelineRow>();


		if (items.Count > 0
			&& items[0] is BoundaryItem accessWalk)
		{
			// The walk starts where the passenger searched from. Without that place (or when it is
			// the very stop the vehicle leaves from) there is no separate starting point to show.
			if (ShowsWalk(accessWalk, options)
				&& accessWalk.Origin is { } start
				&& !WalkStaysInStop(items, 0, accessWalk))
			{
				rows.Add(
					EndpointStop(
						start.Name,
						PlaceOf(start),
						journey.Departure,
						Colors.Transparent,
						WalkColor()));
			}
		}
		else if (items.Count > 0
			&& items[0] is WalkItem firstWalk)
		{
			rows.Add(
				EndpointStop(
					firstWalk.Leg.From.Name,
					PlaceOf(firstWalk.Leg.From),
					firstWalk.EffectiveDeparture
						?? firstWalk.Leg.EffectiveDeparture,
					Colors.Transparent,
					WalkColor(),
					options.ShowPlatforms
						? PlatformText(
							firstWalk.Leg.DeparturePlatform,
							firstWalk.Leg.DeparturePlatformKind)
						: null));
		}


		for (int i = 0; i < items.Count; i++)
		{
			switch (items[i])
			{
				case RideItem ride:
					AddRide(
						rows,
						items,
						i,
						ride.Leg,
						options);
					break;


				case WalkItem walk:
					if (options.ShowWalking)
					{
						string walkTime =
							Format.Duration(
								walk.EffectiveDeparture
									?? walk.Leg.EffectiveDeparture,
								walk.EffectiveArrival
									?? walk.Leg.EffectiveArrival);


						JourneyStrings strings =
							LocalizationService.Current
								.CurrentStrings
								.Journey;


						// A footpath between two stops of the same name (another platform of the
						// same stop, or the provider's own access path) must not read as
						// "walk to the place you are already at".
						bool insideStop =
							SameStation(
								walk.Leg.From,
								walk.Leg.To);


						string? platforms =
							insideStop
								&& options.ShowPlatforms
								? PlatformPair(
									walk.Leg.DeparturePlatform,
									walk.Leg.DeparturePlatformKind,
									walk.Leg.ArrivalPlatform,
									walk.Leg.ArrivalPlatformKind)
								: null;


						rows.Add(
							new WalkRow
							{
								Text =
									$"{strings.Walk} {walkTime}",

								CaptionPrefix =
									insideStop
										? strings.WithinStop
										: strings.To,

								CaptionName =
									insideStop
										? platforms ?? string.Empty
										: walk.Leg.To.Name,

								CaptionPlace =
									insideStop
										? null
										: PlaceOf(walk.Leg.To),

								RailBottom =
									WalkColor(),

								Description =
									$"{strings.Walk} {walkTime} " +
									(insideStop
										? $"{strings.WithinStop} {platforms}".TrimEnd()
										: $"{strings.To} {StopLabel.Compose(walk.Leg.To)}")
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


		if (items.Count > 0
			&& items[^1] is WalkItem lastWalk)
		{
			rows.Add(
				EndpointStop(
					lastWalk.Leg.To.Name,
					PlaceOf(lastWalk.Leg.To),
					lastWalk.EffectiveArrival
						?? lastWalk.Leg.EffectiveArrival,
					WalkColor(),
					Colors.Transparent,
					options.ShowPlatforms
						? PlatformText(
							lastWalk.Leg.ArrivalPlatform,
							lastWalk.Leg.ArrivalPlatformKind)
						: null));
		}
		else if (items.Count > 1
			&& items[^1] is BoundaryItem egressWalk
			&& ShowsWalk(egressWalk, options)
			&& !WalkStaysInStop(items, items.Count - 1, egressWalk))
		{
			rows.Add(
				EndpointStop(
					egressWalk.At.Name,
					PlaceOf(egressWalk.At),
					journey.Arrival,
					WalkColor(),
					Colors.Transparent));
		}


		for (int i = 0; i < rows.Count; i++)
		{
			rows[i].Index =
				i;
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
		Color color =
			ModeColors.For(leg.Mode);


		JourneyStrings strings =
			LocalizationService.Current
				.CurrentStrings
				.Journey;


		bool departureMerged =
			i >= 2
			&& items[i - 1] is BoundaryItem
			&& items[i - 2] is RideItem;


		bool arrivalMerged =
			i + 2 < items.Count
			&& items[i + 1] is BoundaryItem
			&& items[i + 2] is RideItem;


		OccupancyLevel legOccupancy =
			options.ShowOccupancy
				? leg.Vehicle?.Occupancy
					?? OccupancyLevel.Unknown
				: OccupancyLevel.Unknown;


		string? occupancyText =
			FormatOccupancy(
				legOccupancy);


		if (!departureMerged)
		{
			string? delay =
				Format.Delay(
					leg.DepartureDelay);


			OccupancyLevel stopOccupancy =
				OccupancyAt(
					leg.Stops.FirstOrDefault(),
					options);


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

					DelayText =
						delay,

					IsEarly =
						IsEarly(leg.DepartureDelay),

					Name =
						leg.From.Name,

					Place =
						PlaceOf(leg.From),

					PlatformText =
						options.ShowPlatforms
							? PlatformText(
								leg.DeparturePlatform,
								leg.DeparturePlatformKind)
							: null,

					NodeColor =
						color,

					Occupancy =
						stopOccupancy,

					RailTop =
						i == 1
						&& items[0] is BoundaryItem accessWalk
						&& ShowsWalk(accessWalk, options)
						&& !WalkStaysInStop(items, 0, accessWalk)
							? WalkColor()
							: RailAt(
								items,
								i - 1,
								-1),

					RailBottom =
						color,

					Description =
						$"{Format.TimeOrDash(leg.EffectiveDeparture)}, " +
						$"{strings.Depart} {StopLabel.Compose(leg.From)}" +
						Spoken(
							stopOccupancy,
							PlatformText(
								leg.DeparturePlatform,
								leg.DeparturePlatformKind))
				});
		}


		DateTime now =
			DateTime.Now;


		var legRow =
			new LegRow
			{
				LineText =
					leg.Line?.Name
					?? Format.TransportMode(
						leg.Mode),

				ModeColor =
					color,

				Look =
					ModeChips.For(leg.Mode),

				Direction =
					string.IsNullOrWhiteSpace(
						leg.Line?.Destination)
						? null
						: $"{strings.To} " +
							$"{leg.Line!.Destination}",

				DurationText =
					Format.Duration(
						leg.EffectiveDeparture,
						leg.EffectiveArrival),

				FeaturesText =
					Features(
						leg.Vehicle?.Accessibility),

				Occupancy =
					legOccupancy,

				OccupancyText =
					occupancyText,

				IsCancelled =
					leg.IsCancelled,

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

									Place =
										PlaceOf(
											stop.Station),

									DelayText =
										Format.Delay(
											stop.DepartureDelay
											?? stop.ArrivalDelay),

									IsEarly =
										IsEarly(
											stop.DepartureDelay
											?? stop.ArrivalDelay),

									IsNotServed =
										stop.IsCancelled,

									Occupancy =
										stop.IsCancelled
											? OccupancyLevel.Unknown
											: OccupancyAt(
												stop,
												options),

									RailBottom =
										color,

									Description =
										$"{TimeOf(stop.EffectiveDeparture ?? stop.EffectiveArrival)}, " +
										StopLabel.Compose(stop.Station) +
										Spoken(
											stop.IsCancelled
												? OccupancyLevel.Unknown
												: OccupancyAt(
													stop,
													options),
											null)
								})
						.ToList(),

				RailBottom =
					color,

				Description =
					$"{leg.Line?.Name ?? Format.TransportMode(leg.Mode)}, " +
					Format.Duration(
						leg.EffectiveDeparture,
						leg.EffectiveArrival)
			};


		rows.Add(
			legRow);


		// "Show intermediate stops by default": the rows are inserted exactly as a tap would.
		if (options.ExpandStops
			&& legRow.HasIntermediates)
		{
			legRow.IsExpanded =
				true;

			rows.AddRange(
				legRow.Intermediates);
		}


		foreach (string notice in leg.Notices.Distinct())
		{
			rows.Add(
				new NoticeRow
				{
					Text =
						notice,

					RailBottom =
						color,

					Description =
						notice,

					Expanded =
						options.ExpandNotices,

					Technical =
						options.Technical
				});
		}


		if (!arrivalMerged)
		{
			string? delay =
				Format.Delay(
					leg.ArrivalDelay);


			OccupancyLevel lastStopOccupancy =
				OccupancyAt(
					leg.Stops.LastOrDefault(),
					options);


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

					DelayText =
						delay,

					IsEarly =
						IsEarly(leg.ArrivalDelay),

					Name =
						leg.To.Name,

					Place =
						PlaceOf(leg.To),

					PlatformText =
						options.ShowPlatforms
							? PlatformText(
								leg.ArrivalPlatform,
								leg.ArrivalPlatformKind)
							: null,

					NodeColor =
						color,

					Occupancy =
						lastStopOccupancy,

					RailTop =
						color,

					RailBottom =
						i + 2 == items.Count
						&& items[i + 1] is BoundaryItem egressWalk
						&& ShowsWalk(egressWalk, options)
						&& !WalkStaysInStop(items, i + 1, egressWalk)
							? WalkColor()
							: RailAt(
								items,
								i + 1,
								+1),

					Description =
						$"{Format.TimeOrDash(leg.EffectiveArrival)}, " +
						$"{strings.Arrive} {StopLabel.Compose(leg.To)}" +
						Spoken(
							lastStopOccupancy,
							PlatformText(
								leg.ArrivalPlatform,
								leg.ArrivalPlatformKind))
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
			RailAt(
				items,
				i + 1,
				+1);


		JourneyStrings strings =
			LocalizationService.Current
				.CurrentStrings
				.Journey;


		if (between)
		{
			JourneyLeg from =
				((RideItem)items[i - 1]).Leg;


			JourneyLeg to =
				((RideItem)items[i + 1]).Leg;


			// The row already names the place under the stop, so the continuation only repeats
			// the city when the next stop really is in another one.
			string continueName =
				string.Equals(
					from.To.Place,
					to.From.Place,
					StringComparison.OrdinalIgnoreCase)
					? to.From.Name
					: StopLabel.Compose(to.From);


			string? connectionText =
				JoinText(
					from.To.Name == to.From.Name
						? null
						: $"{strings.ContinueFrom} {continueName}",
					options.ShowPlatforms
						? PlatformPair(
							from.ArrivalPlatform,
							from.ArrivalPlatformKind,
							to.DeparturePlatform,
							to.DeparturePlatformKind)
						: null);


			OccupancyLevel arrivalOccupancy =
				OccupancyAt(
					from.Stops.LastOrDefault(),
					options);


			OccupancyLevel departureOccupancy =
				OccupancyAt(
					to.Stops.FirstOrDefault(),
					options);


			rows.Add(
				new InterchangeRow
				{
					ArrivalTime =
						Format.TimeOrDash(
							from.EffectiveArrival),

					ArrivalDelay =
						Format.Delay(
							from.ArrivalDelay),

					DepartureTime =
						Format.TimeOrDash(
							to.EffectiveDeparture),

					DepartureDelay =
						Format.Delay(
							to.DepartureDelay),

					ArrivalIsEarly =
						IsEarly(from.ArrivalDelay),

					DepartureIsEarly =
						IsEarly(to.DepartureDelay),

					Name =
						from.To.Name,

					Place =
						PlaceOf(from.To),

					ArrivalOccupancy =
						arrivalOccupancy,

					DepartureOccupancy =
						departureOccupancy,

					ConnectionText =
						connectionText,

					WaitText =
						boundary.ShowWait
							? boundary.Wait < TimeSpan.FromMinutes(1)
								? strings.ImmediateChange
								: $"{Format.Duration(boundary.Wait)} " +
									$"{strings.ToChange}"
							: null,

					WalkText =
						options.ShowWalking
						&& boundary.WalkTime is { } walk
							? $"{strings.WalkAbout} " +
								$"{Format.Duration(walk)} " +
								$"{strings.BetweenStops}"
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
						$"{strings.ChangeAt} {StopLabel.Compose(from.To)}, " +
						Format.Duration(boundary.Wait) +
						Spoken(
							arrivalOccupancy,
							null) +
						Spoken(
							departureOccupancy,
							null)
				});
		}
		else if (ShowsWalk(boundary, options)
			&& boundary.WalkTime is { } walkTime)
		{
			// A walk between two points of the same stop must not read as "walk to where you are".
			bool insideStop =
				WalkStaysInStop(
					items,
					i,
					boundary);

			rows.Add(
				new WalkRow
				{
					Text =
						$"{strings.Walk} " +
						Format.Duration(walkTime),

					CaptionPrefix =
						insideStop
							? strings.WithinStop
							: strings.To,

					CaptionName =
						insideStop
							? string.Empty
							: boundary.At.Name,

					CaptionPlace =
						insideStop
							? null
							: PlaceOf(boundary.At),

					// Without a starting/ending point to connect to, an in-stop walk draws no rail.
					RailTop =
						i == 0
							? Colors.Transparent
							: RailAt(
								items,
								i - 1,
								-1),

					RailBottom =
						insideStop
							? Colors.Transparent
							: WalkColor(),

					Description =
						$"{strings.Walk} " +
						Format.Duration(walkTime) +
						(insideStop
							? $" {strings.WithinStop}"
							: $" {strings.To} {StopLabel.Compose(boundary.At)}")
				});
		}


		foreach (string note in boundary.Notes)
		{
			rows.Add(
				new NoticeRow
				{
					Text =
						note,

					RailBottom =
						next,

					Description =
						note,

					Expanded =
						options.ExpandNotices,

					Technical =
						options.Technical
				});
		}
	}


	/// <summary>
	/// A walk before the first / after the last vehicle that begins or ends at the very stop the
	/// vehicle uses (only the platform changes), so there is no separate point to draw.
	/// </summary>
	private static bool WalkStaysInStop(
		IReadOnlyList<TimelineItem> items,
		int index,
		BoundaryItem boundary) =>
		index == 0
			? boundary.Origin is { } origin
				&& SameStation(origin, boundary.At)
			: index == items.Count - 1
				&& items[index - 1] is RideItem ride
				&& SameStation(ride.Leg.To, boundary.At);


	/// <summary>A walk before the first or after the last vehicle that the timeline shows.</summary>
	private static bool ShowsWalk(
		BoundaryItem boundary,
		TimelineOptions options) =>
		options.ShowWalking
		&& boundary.WalkTime is { } walk
		&& walk > TimeSpan.Zero;


	private static StopRow EndpointStop(
		string name,
		string? place,
		DateTimeOffset? time,
		Color top,
		Color bottom,
		string? platform = null) =>
		new()
		{
			Place =
				place,

			PlatformText =
				platform,

			Time =
				Format.TimeOrDash(time),

			Name =
				name,

			NodeColor =
				WalkColor(),

			RailTop =
				top,

			RailBottom =
				bottom,

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
		while (
			index >= 0
			&& index < items.Count)
		{
			switch (items[index])
			{
				case RideItem ride:
					return ModeColors.For(
						ride.Leg.Mode);

				case WalkItem:
					return WalkColor();
			}

			index += step;
		}


		return Colors.Transparent;
	}


	private static string? JoinText(
		params string?[] values)
	{
		string[] parts =
	values
		.Where(value => !string.IsNullOrWhiteSpace(value))
		.Select(value => value!)
		.ToArray();


		return parts.Length == 0
			? null
			: string.Join(
				" \u00b7 ",
				parts);
	}


	/// <summary>
	/// The city/village/region delivered with a stop (shown behind the name);
	/// null when absent or when the name already ends with it.
	/// </summary>
	private static string? PlaceOf(
		Station station) =>
		StopLabel.PlaceFor(
			station.Name,
			station.Place);


	/// <summary>Two stop references that mean the same place (same provider id, or same name and city).</summary>
	private static bool SameStation(
		Station a,
		Station b) =>
		!string.IsNullOrWhiteSpace(a.Id)
		&& !string.IsNullOrWhiteSpace(b.Id)
			? string.Equals(
				a.Id,
				b.Id,
				StringComparison.OrdinalIgnoreCase)
			: string.Equals(
				a.Name,
				b.Name,
				StringComparison.CurrentCultureIgnoreCase)
				&& string.Equals(
					a.Place,
					b.Place,
					StringComparison.CurrentCultureIgnoreCase);


	private static bool IsEarly(
		TimeSpan? delay) =>
		delay is { } value
		&& value < TimeSpan.Zero;


	private static OccupancyLevel OccupancyAt(
		StopTime? stop,
		TimelineOptions options) =>
		options.ShowOccupancy
			&& stop is { IsCancelled: false }
				? stop.Occupancy
				: OccupancyLevel.Unknown;


	/// <summary>
	/// Appends occupancy and platform to a spoken description, so screen-reader users
	/// get what sighted users read off the dots.
	/// </summary>
	private static string Spoken(
		OccupancyLevel occupancy,
		string? platform)
	{
		string result =
			string.Empty;

		if (platform is not null)
		{
			result +=
				$", {platform}";
		}

		if (FormatOccupancy(occupancy) is { } text)
		{
			result +=
				$", {LocalizationService.Current.CurrentStrings.Journey.Occupancy}: {text}";
		}

		return result;
	}


	private static Color WalkColor() =>
		ModeColors
			.For(TransitMode.Walk)
			.WithAlpha(0.5f);


	private static string TimeOf(
		DateTimeOffset? time) =>
		time is { } value
			? Format.Time(value)
			: Dash;


	/// <summary>
	/// "Steig 3" for a platform, "Gleis 3" for a railway track. When the provider did not say
	/// which, a short name is shown as "Steig/Gleis 3" rather than guessing; a longer one is shown as given.
	/// </summary>
	private static string? PlatformText(
		string? platform,
		PlatformKind kind)
	{
		if (string.IsNullOrWhiteSpace(platform))
		{
			return null;
		}


		platform =
			platform.Trim();


		JourneyStrings strings =
			LocalizationService.Current
				.CurrentStrings
				.Journey;


		string? label =
			kind switch
			{
				PlatformKind.Platform =>
					strings.Platform,

				PlatformKind.Railtrack =>
					strings.Track,

				_ =>
					platform.Length <= 3
						? $"{strings.Platform}/{strings.Track}"
						: null
			};


		return label is null
			|| platform.StartsWith(
				label,
				StringComparison.OrdinalIgnoreCase)
			? platform
			: $"{label} {platform}";
	}


	private static string? PlatformPair(
		string? arrival,
		PlatformKind arrivalKind,
		string? departure,
		PlatformKind departureKind)
	{
		if (string.IsNullOrWhiteSpace(arrival)
			&& string.IsNullOrWhiteSpace(departure))
		{
			return null;
		}


		if (string.IsNullOrWhiteSpace(arrival))
		{
			return PlatformText(
				departure,
				departureKind);
		}


		if (string.IsNullOrWhiteSpace(departure))
		{
			return PlatformText(
				arrival,
				arrivalKind);
		}


		bool sameKind =
			arrivalKind == departureKind
			|| arrivalKind == PlatformKind.Unknown
			|| departureKind == PlatformKind.Unknown;


		if (sameKind
			&& string.Equals(
				arrival,
				departure,
				StringComparison.OrdinalIgnoreCase))
		{
			return PlatformText(
				arrival,
				arrivalKind != PlatformKind.Unknown
					? arrivalKind
					: departureKind);
		}


		return
			$"{PlatformText(arrival, arrivalKind)} \u2192 " +
			$"{PlatformText(departure, departureKind)}";
	}


	private static string? Features(
		AccessibilityInfo? info)
	{
		if (info is null)
		{
			return null;
		}


		JourneyStrings strings =
			LocalizationService.Current
				.CurrentStrings
				.Journey;


		var values =
			new List<string>();


		if (info.LowFloor == true)
		{
			values.Add(
				strings.LowFloor);
		}


		if (info.WheelchairAccessible == true)
		{
			values.Add(
				strings.WheelchairAccessible);
		}


		if (info.BicycleAccessible == true)
		{
			values.Add(
				strings.BicycleAccessible);
		}


		return values.Count == 0
			? null
			: string.Join(
				" \u00b7 ",
				values);
	}


	private static string? FormatOccupancy(
		OccupancyLevel? occupancy)
	{
		if (occupancy is null
			|| occupancy == OccupancyLevel.Unknown)
		{
			return null;
		}


		JourneyStrings strings =
			LocalizationService.Current
				.CurrentStrings
				.Journey;


		return occupancy switch
		{
			OccupancyLevel.VeryLow =>
				strings.OccupancyVeryLow,

			OccupancyLevel.Low =>
				strings.OccupancyLow,

			OccupancyLevel.Medium =>
				strings.OccupancyMedium,

			OccupancyLevel.High =>
				strings.OccupancyHigh,

			OccupancyLevel.Full =>
				strings.OccupancyFull,

			OccupancyLevel.Overloaded =>
				strings.OccupancyOverloaded,

			_ =>
				null
		};
	}
}