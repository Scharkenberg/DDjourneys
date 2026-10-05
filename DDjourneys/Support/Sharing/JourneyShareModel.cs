using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support.Sharing;

public enum ShareStepKind
{
	/// <summary>The starting point (always present unless the first ride boards exactly there).</summary>
	Depart,

	/// <summary>A walk (before, between or after rides).</summary>
	Walk,

	/// <summary>A ride on one vehicle.</summary>
	Ride,

	/// <summary>A change between two rides.</summary>
	Change,

	/// <summary>Arrival at the destination.</summary>
	Arrive
}

/// <summary>One step of a shared journey. Which fields are set depends on <see cref="Kind"/>.</summary>
public sealed record ShareStep
{
	public required ShareStepKind Kind { get; init; }

	/// <summary>Planned time the step begins (ride: departure; walk: start; arrive: arrival).</summary>
	public DateTimeOffset? Time { get; init; }

	/// <summary>Real-time version of <see cref="Time"/>, only when it differs from the plan.</summary>
	public DateTimeOffset? LiveTime { get; init; }

	public TransitMode Mode { get; init; } = TransitMode.Unknown;

	/// <summary>Line name ("11"), or the mode's name when the line has none.</summary>
	public string Line { get; init; } = string.Empty;

	public string? Direction { get; init; }

	/// <summary>Where the step starts (ride: boarding stop; change: the stop).</summary>
	public string From { get; init; } = string.Empty;

	public string? FromPlatform { get; init; }

	/// <summary>Where the step ends (ride: alighting stop; walk: target).</summary>
	public string To { get; init; } = string.Empty;

	public string? ToPlatform { get; init; }

	/// <summary>Ride: planned arrival.</summary>
	public DateTimeOffset? EndTime { get; init; }

	/// <summary>Ride: real-time arrival, only when it differs from the plan.</summary>
	public DateTimeOffset? EndLiveTime { get; init; }

	/// <summary>Ride: stops between boarding and alighting.</summary>
	public int IntermediateStops { get; init; }

	/// <summary>Walk: walking time. Change: time left to change.</summary>
	public TimeSpan? Duration { get; init; }

	/// <summary>Walk between two stops of a change: time left after the walk.</summary>
	public TimeSpan? WaitTime { get; init; }

	/// <summary>Change: walking time within the change.</summary>
	public TimeSpan? WalkTime { get; init; }

	public bool IsCancelled { get; init; }

	/// <summary>Change (or walk between stops of a change): the connection may be missed.</summary>
	public bool IsEndangered { get; init; }

	/// <summary>Change (or walk between stops of a change): the operators guarantee the connection (it is held).</summary>
	public bool IsGuaranteed { get; init; }
}

/// <summary>
/// Everything a shared journey shows, independent of the medium. The text and the image are both
/// rendered from this model, so they never disagree.
/// </summary>
public sealed record JourneyShareModel
{
	public required string Origin { get; init; }

	public required string Destination { get; init; }

	/// <summary>"Monday, 5 October".</summary>
	public required string Day { get; init; }

	public DateTimeOffset? Departure { get; init; }

	public DateTimeOffset? Arrival { get; init; }

	public required string DurationText { get; init; }

	public required string TransfersText { get; init; }

	public required IReadOnlyList<ShareStep> Steps { get; init; }

	/// <summary>The ticket price for the passenger set in the options ("2,70 €"); null when the provider quotes none.</summary>
	public string? PriceText { get; init; }

	/// <summary>The journey cannot take place (a ride cancelled, a stop skipped, a connection unreachable).</summary>
	public bool IsCancelled { get; init; }

	/// <summary>"Not possible · Schweriner Straße is not served"; empty when the journey is possible.</summary>
	public string BlockText { get; init; } = string.Empty;

	/// <summary>The reason alone ("Schweriner Straße is not served"); empty when the journey is possible.</summary>
	public string BlockReason { get; init; } = string.Empty;

	/// <summary>Lines in riding order, for chips ("11", "62").</summary>
	public IEnumerable<ShareStep> Rides => Steps.Where(step => step.Kind == ShareStepKind.Ride);

	public static JourneyShareModel Create(
		Journey journey,
		IUiStrings strings,
		PassengerCategory passenger = PassengerCategory.Adult)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(strings);

		JourneyStrings text = strings.Journey;

		Station start = journey.Origin ?? journey.From;
		Station end = journey.Destination ?? journey.To;

		var steps = new List<ShareStep>();
		IReadOnlyList<TimelineItem> items = TimelineBuilder.Build(journey);

		for (int i = 0; i < items.Count; i++)
		{
			switch (items[i])
			{
				case RideItem { Leg: var leg }:
					steps.Add(Ride(leg, text));
					break;

				case WalkItem walk:
					steps.Add(
						new ShareStep
						{
							Kind = ShareStepKind.Walk,
							Time = walk.EffectiveDeparture ?? walk.Leg.EffectiveDeparture,
							Duration =
								(walk.EffectiveArrival ?? walk.Leg.EffectiveArrival)
								- (walk.EffectiveDeparture ?? walk.Leg.EffectiveDeparture),
							To = StopLabel.Compose(walk.Leg.To)
						});
					break;

				case BoundaryItem { ShowWait: true } change
					when i > 0
						&& i + 1 < items.Count
						&& items[i - 1] is RideItem before
						&& items[i + 1] is RideItem after
						&& !SameStation(before.Leg.To, after.Leg.From):
					// A change that walks to another stop: alight, walk to the boarding stop, board.
					steps.Add(
						new ShareStep
						{
							Kind = ShareStepKind.Walk,
							Time = before.Leg.EffectiveArrival,
							Duration = change.WalkTime,
							WaitTime = change.Wait,
							To = StopLabel.Compose(after.Leg.From),
							IsEndangered = change.Endangered,
							IsGuaranteed = change.Ensured
						});
					break;

				case BoundaryItem { ShowWait: true } change:
					steps.Add(
						new ShareStep
						{
							Kind = ShareStepKind.Change,
							From = StopLabel.Compose(change.At),
							Duration = change.Wait,
							WalkTime = change.WalkTime,
							IsEndangered = change.Endangered,
							IsGuaranteed = change.Ensured
						});
					break;

				case BoundaryItem { WalkTime: { } walkTime } boundary:
					// A walk to the first stop (nothing before it yet) or from the last one.
					steps.Add(
						new ShareStep
						{
							Kind = ShareStepKind.Walk,
							Time =
								steps.Count == 0
									? journey.Departure
									: journey.Arrival - walkTime,
							Duration = walkTime,
							To = StopLabel.Compose(boundary.At)
						});
					break;
			}
		}

		// The starting point is always part of the picture, whatever it is (stop, address, POI, coordinates);
		// only a first ride that boards exactly there already shows it.
		string startLabel = StopLabel.Compose(start);

		if (!(steps.Count > 0
			&& steps[0] is { Kind: ShareStepKind.Ride } first
			&& string.Equals(first.From, startLabel, StringComparison.CurrentCultureIgnoreCase)))
		{
			steps.Insert(
				0,
				new ShareStep
				{
					Kind = ShareStepKind.Depart,
					Time = journey.Departure,
					To = startLabel
				});
		}

		steps.Add(
			new ShareStep
			{
				Kind = ShareStepKind.Arrive,
				Time = journey.Arrival,
				To = StopLabel.Compose(end)
			});

		int transfers = journey.TransferCount;

		return new JourneyShareModel
		{
			Origin = startLabel,
			Destination = StopLabel.Compose(end),
			Day =
				journey.Departure is { } departure
					? Format.ToWall(departure).ToString("dddd, d MMMM", CultureInfo.CurrentCulture)
					: string.Empty,
			Departure = journey.Departure,
			Arrival = journey.Arrival,
			DurationText = Format.Duration(journey.Duration),
			TransfersText =
				transfers switch
				{
					0 => text.Direct,
					1 => text.OneTransfer,
					_ => string.Format(CultureInfo.CurrentCulture, text.MultipleTransfers, transfers)
				},
			Steps = steps,
			PriceText =
				FareChoice.Preferred(journey.Fares, passenger) is { Price: { } price } fare
					? Format.Price(price, fare.Currency)
					: null,
			IsCancelled = journey.IsImpossible,
			BlockText = JourneyBlockText.Describe(journey.Block, text),
			BlockReason = JourneyBlockText.Reason(journey.Block, text)
		};
	}

	private static bool SameStation(Station a, Station b) =>
		!string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(b.Id)
			? string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase)
			: string.Equals(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
				&& string.Equals(a.Place, b.Place, StringComparison.CurrentCultureIgnoreCase);

	private static ShareStep Ride(JourneyLeg leg, JourneyStrings text) =>
		new()
		{
			Kind = ShareStepKind.Ride,
			Mode = leg.Mode,
			Line = leg.Line?.Name is { Length: > 0 } name ? name : Format.TransportMode(leg.Mode),
			Direction = leg.Line?.Destination,
			From = StopLabel.Compose(leg.From),
			FromPlatform = Platform(leg.DeparturePlatform, leg.DeparturePlatformKind, text),
			Time = leg.ScheduledDeparture ?? leg.EffectiveDeparture,
			LiveTime = Differs(leg.RealtimeDeparture, leg.ScheduledDeparture),
			To = StopLabel.Compose(leg.To),
			ToPlatform = Platform(leg.ArrivalPlatform, leg.ArrivalPlatformKind, text),
			EndTime = leg.ScheduledArrival ?? leg.EffectiveArrival,
			EndLiveTime = Differs(leg.RealtimeArrival, leg.ScheduledArrival),
			IntermediateStops = Math.Max(0, leg.Stops.Count - 2),
			IsCancelled = leg.IsCancelled
		};

	/// <summary>The real-time value when it differs by at least a minute; null otherwise.</summary>
	private static DateTimeOffset? Differs(DateTimeOffset? realtime, DateTimeOffset? planned) =>
		realtime is { } live
		&& planned is { } plan
		&& Math.Abs((live - plan).TotalMinutes) >= 1
			? live
			: null;

	private static string? Platform(string? platform, PlatformKind kind, JourneyStrings text) =>
		string.IsNullOrWhiteSpace(platform)
			? null
			: $"{(kind == PlatformKind.Railtrack ? text.Track : text.Platform)} {platform.Trim()}";
}
