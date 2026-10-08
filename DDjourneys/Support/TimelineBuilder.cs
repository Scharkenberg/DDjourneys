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
	TimeSpan? WalkTime = null,
	Station? Origin = null,
	bool Ensured = false) : TimelineItem;

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
			return (TimelineItem[])[];
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
			// The walk leads TO the first vehicle stop; where it starts is the requested origin.
			items.Add(
				CreateBoundary(
					journey.From,
					initialTransfers,
					showWait: false)
				with
				{
					Origin = journey.Origin
				});
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
							journey.Destination ?? journey.To,
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
				leg.IsRide
				&& next.IsRide;

			if (transfers.Length > 0
				|| isInterchange)
			{
				items.Add(
					CreateBoundary(
						leg.To,
						transfers,
						showWait: isInterchange,
						nextLeg: next,
						previousArrival: previousEffectiveArrival,
						previousLeg: leg));
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
		DateTimeOffset? previousArrival = null,
		JourneyLeg? previousLeg = null)
	{
		TimeSpan? walkTime =
			GetWalkingDuration(transfers);

		DateTimeOffset? nextDeparture =
			nextLeg?.EffectiveDeparture;

		TimeSpan wait =
			DetermineWait(
				transfers,
				previousArrival,
				nextDeparture,
				walkTime);

		// With realtime data the connection can already be lost: the next
		// vehicle leaves before the passenger can get there.
		// An ensured connection waits for the arriving vehicle: whatever the clock says, it is not lost.
		bool ensured =
			transfers.Any(transfer => transfer.IsEnsured);

		bool missed =
			showWait
			&& !ensured
			&& previousArrival is { } arrives
			&& nextDeparture is { } departs
			&& departs < arrives + (walkTime ?? TimeSpan.Zero);

		string[] notes =
			[.. transfers
				.SelectMany(transfer => transfer.Notices)
				.Where(n => !string.IsNullOrWhiteSpace(n))
				.Distinct()];

		// With real-time times on either side the arithmetic decides: the next vehicle leaves after the previous one
		// arrived plus the walk, so the change is not endangered, whatever the provider's timetable-based flag says
		// (a vehicle that is late on departure does not endanger a change). Without real-time data the provider's
		// flag stands.
		bool live =
			previousLeg?.RealtimeArrival is not null
			|| nextLeg?.RealtimeDeparture is not null;

		bool computable =
			showWait
			&& previousArrival is not null
			&& nextDeparture is not null;

		bool endangered =
			!ensured
			&& (computable && live
				? missed
				: missed
					|| transfers.Any(transfer => !transfer.IsGuaranteed));

		return new BoundaryItem(
			location,
			wait,
			notes,
			showWait,
			endangered,
			walkTime,
			Ensured: ensured);
	}

	/// <summary>
	/// Time left to change. The live window between the effective arrival
	/// and departure wins, because the provider's own waiting time is based
	/// on the timetable and goes stale as soon as a vehicle is late.
	/// </summary>
	private static TimeSpan DetermineWait(
		IReadOnlyList<JourneyTransfer> transfers,
		DateTimeOffset? previousArrival,
		DateTimeOffset? nextDeparture,
		TimeSpan? walkTime)
	{
		if (previousArrival is { } arrives
			&& nextDeparture is { } departs)
		{
			TimeSpan window =
				departs - arrives - (walkTime ?? TimeSpan.Zero);

			return window > TimeSpan.Zero
				? window
				: TimeSpan.Zero;
		}

		// No usable times: fall back to what the provider reported.
		TimeSpan explicitWaiting = TimeSpan.Zero;

		foreach (JourneyTransfer transfer in transfers)
		{
			if (transfer.WaitingTime is { } waiting
				&& waiting > TimeSpan.Zero)
			{
				explicitWaiting += waiting;
			}
		}

		return explicitWaiting;
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
		[.. journey.Transfers
			.Where(
				transfer =>
					transfer.PreviousLegIndex
						== previousLegIndex
					&& transfer.NextLegIndex
						== nextLegIndex)];
}