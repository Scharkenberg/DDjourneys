using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support.Sharing;

public enum ShareStepKind
{
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

	/// <summary>Change: walking time within the change.</summary>
	public TimeSpan? WalkTime { get; init; }

	public bool IsCancelled { get; init; }

	/// <summary>Change: the connection may be missed.</summary>
	public bool IsEndangered { get; init; }
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

	/// <summary>Plain-text notices, each at most once.</summary>
	public required IReadOnlyList<string> Notices { get; init; }

	public bool IsCancelled { get; init; }

	/// <summary>Lines in riding order, for chips ("11", "62").</summary>
	public IEnumerable<ShareStep> Rides => Steps.Where(step => step.Kind == ShareStepKind.Ride);

	public static JourneyShareModel Create(Journey journey, IUiStrings strings)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(strings);

		JourneyStrings text = strings.Journey;

		Station start = journey.Origin ?? journey.From;
		Station end = journey.Destination ?? journey.To;

		var steps = new List<ShareStep>();

		foreach (TimelineItem item in TimelineBuilder.Build(journey))
		{
			switch (item)
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

				case BoundaryItem { ShowWait: true } change:
					steps.Add(
						new ShareStep
						{
							Kind = ShareStepKind.Change,
							From = StopLabel.Compose(change.At),
							Duration = change.Wait,
							WalkTime = change.WalkTime,
							IsEndangered = change.Endangered
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
			Origin = StopLabel.Compose(start),
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
			Notices = CollectNotices(journey),
			IsCancelled = journey.IsCancelled
		};
	}

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

	private static IReadOnlyList<string> CollectNotices(Journey journey) =>
		journey.Notices
			.Concat(journey.Legs.SelectMany(leg => leg.Notices))
			.Concat(journey.Transfers.SelectMany(transfer => transfer.Notices))
			.Where(notice => !string.IsNullOrWhiteSpace(notice))
			.Select(notice => NoticeText.Plain(NoticeText.Parse(notice)).Trim())
			.Where(notice => notice.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.ToList();
}
