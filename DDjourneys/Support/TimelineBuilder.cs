using DDjourneys.Core.Models;

namespace DDjourneys.Support;

public abstract record TimelineItem;

public sealed record RideItem(
	JourneyLeg Leg) : TimelineItem;

public sealed record WalkItem(
	JourneyLeg Leg,
	DateTimeOffset? EffectiveDeparture = null,
	DateTimeOffset? EffectiveArrival = null) : TimelineItem;

/// <summary>
/// The boundary between two journey legs, or a terminal walking transfer
/// at the beginning or end of the journey.
/// </summary>
public sealed record BoundaryItem(
	Station At,
	TimeSpan Wait,
	IReadOnlyList<string> Notes,
	bool ShowWait,
	bool Endangered = false,
	TimeSpan? WalkTime = null) : TimelineItem;

/// <summary>
/// Converts the provider-neutral journey model into a flat, display-ready
/// sequence. Transfer ownership is determined exclusively by leg indices.
/// </summary>
public static class TimelineBuilder
{
	public static IReadOnlyList<TimelineItem> Build(
		Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		if (journey.Legs.Count == 0)
		{
			return Array.Empty<TimelineItem>();
		}

		var items =
			new List<TimelineItem>(
				journey.Legs.Count * 2 + 2);

		// A transfer before the first movement leg is an access transfer.
		JourneyTransfer[] initialTransfers =
			GetTransfers(
				journey,
				previousLegIndex: null,
				nextLegIndex: 0);

		if (initialTransfers.Length > 0)
		{
			items.Add(
				CreateBoundary(
					journey.From,
					initialTransfers,
					showWait: false));
		}

		DateTimeOffset? previousEffectiveArrival = null;

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			JourneyLeg leg =
				journey.Legs[i];

			if (leg.Mode == TransitMode.Walk)
			{
				WalkItem walk =
					CreateWalkItem(
						leg,
						previousEffectiveArrival);

				items.Add(walk);

				previousEffectiveArrival =
					walk.EffectiveArrival
					?? leg.EffectiveArrival;
			}
			else
			{
				items.Add(
					new RideItem(leg));

				previousEffectiveArrival =
					leg.EffectiveArrival;
			}

			if (i == journey.Legs.Count - 1)
			{
				JourneyTransfer[] finalTransfers =
					GetTransfers(
						journey,
						previousLegIndex: i,
						nextLegIndex: null);

				if (finalTransfers.Length > 0)
				{
					items.Add(
						CreateBoundary(
							journey.To,
							finalTransfers,
							showWait: false));
				}

				break;
			}

			JourneyLeg next =
				journey.Legs[i + 1];

			JourneyTransfer[] transfers =
				GetTransfers(
					journey,
					previousLegIndex: i,
					nextLegIndex: i + 1);

			bool isInterchange =
				leg.Mode != TransitMode.Walk
				&& next.Mode != TransitMode.Walk;

			if (transfers.Length > 0
				|| isInterchange)
			{
				items.Add(
					CreateBoundary(
						leg.To,
						transfers,
						showWait: isInterchange,
						nextLeg: next,
						previousArrival: previousEffectiveArrival));
			}
		}

		return items;
	}

	private static WalkItem CreateWalkItem(
		JourneyLeg leg,
		DateTimeOffset? previousEffectiveArrival)
	{
		DateTimeOffset? departure =
			leg.EffectiveDeparture;

		DateTimeOffset? arrival =
			leg.EffectiveArrival;

		// Walking time is treated as a movable duration. When the preceding
		// leg is delayed past the planned walking departure, shift the entire
		// walking segment forward instead of preserving an impossible overlap.
		if (previousEffectiveArrival is { } priorArrival
			&& leg.ScheduledDeparture is { } scheduledDeparture
			&& leg.ScheduledArrival is { } scheduledArrival
			&& scheduledArrival >= scheduledDeparture)
		{
			TimeSpan duration =
				scheduledArrival - scheduledDeparture;

			departure =
				priorArrival > scheduledDeparture
					? priorArrival
					: scheduledDeparture;

			arrival =
				departure + duration;
		}

		return new WalkItem(
			leg,
			departure,
			arrival);
	}

	private static BoundaryItem CreateBoundary(
		Station location,
		IReadOnlyList<JourneyTransfer> transfers,
		bool showWait,
		JourneyLeg? nextLeg = null,
		DateTimeOffset? previousArrival = null)
	{
		TimeSpan? walkTime =
			GetWalkingDuration(transfers);

		TimeSpan wait =
			DetermineWait(
				transfers,
				previousArrival,
				nextLeg?.EffectiveDeparture,
				walkTime);

		string[] notes =
			transfers
				.SelectMany(
					transfer => transfer.Notices)
				.Where(
					n => !string.IsNullOrWhiteSpace(n))
				.Distinct()
				.ToArray();

		bool endangered =
			transfers.Any(
				transfer => !transfer.IsGuaranteed);

		return new BoundaryItem(
			location,
			wait,
			notes,
			showWait,
			endangered,
			walkTime);
	}

	private static TimeSpan DetermineWait(
		IReadOnlyList<JourneyTransfer> transfers,
		DateTimeOffset? previousArrival,
		DateTimeOffset? nextDeparture,
		TimeSpan? walkTime)
	{
		TimeSpan? explicitWaiting =
			transfers
				.Where(
					transfer =>
						transfer.WaitingTime is { } waiting
						&& waiting >= TimeSpan.Zero)
				.Select(
					transfer =>
						transfer.WaitingTime)
				.Aggregate(
					(TimeSpan?)null,
					(total, value) =>
						total is { } current
							&& value is { } additional
							? current + additional
							: value);

		if (explicitWaiting is { } knownWaiting)
		{
			return knownWaiting;
		}

		if (previousArrival is not { } arrives
			|| nextDeparture is not { } departs
			|| departs < arrives)
		{
			return TimeSpan.Zero;
		}

		TimeSpan connectionWindow =
			departs - arrives;

		if (walkTime is not { } walking
			|| walking <= TimeSpan.Zero)
		{
			return connectionWindow;
		}

		return connectionWindow > walking
			? connectionWindow - walking
			: TimeSpan.Zero;
	}

	private static TimeSpan? GetWalkingDuration(
		IReadOnlyList<JourneyTransfer> transfers)
	{
		TimeSpan total =
			TimeSpan.Zero;

		foreach (JourneyTransfer transfer in transfers)
		{
			if (transfer.Kind != TransferKind.Walk
				|| transfer.Duration <= TimeSpan.Zero)
			{
				continue;
			}

			total += transfer.Duration;
		}

		return total > TimeSpan.Zero
			? total
			: null;
	}

	private static JourneyTransfer[] GetTransfers(
		Journey journey,
		int? previousLegIndex,
		int? nextLegIndex) =>
		journey.Transfers
			.Where(
				transfer =>
					transfer.PreviousLegIndex
						== previousLegIndex
					&& transfer.NextLegIndex
						== nextLegIndex)
			.ToArray();
}