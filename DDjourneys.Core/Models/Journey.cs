namespace DDjourneys.Core.Models;

public sealed class Journey
{
	public required IReadOnlyList<JourneyLeg> Legs { get; init; }

	public required Station From { get; init; }

	public required Station To { get; init; }

	public string? Id { get; init; }

	public string? Context { get; init; }

	public IReadOnlyList<string> Notices { get; init; }
		= Array.Empty<string>();

	/// <summary>
	/// Effective time at which the passenger starts the journey,
	/// including any provider-described transfer before the first leg.
	/// </summary>
	public DateTimeOffset? Departure
	{
		get
		{
			if (Legs.Count == 0
				|| Legs[0].EffectiveDeparture is not { } departure)
			{
				return null;
			}

			TimeSpan accessDuration =
				TerminalTransferDuration(
					previousLegIndex: null,
					nextLegIndex: 0);

			return departure - accessDuration;
		}
	}

	/// <summary>
	/// Effective time at which the passenger reaches the destination,
	/// including any provider-described transfer after the final leg.
	/// </summary>
	public DateTimeOffset? Arrival
	{
		get
		{
			if (Legs.Count == 0
				|| FinalLegArrival() is not { } arrival)
			{
				return null;
			}

			TimeSpan accessDuration =
				TerminalTransferDuration(
					previousLegIndex: Legs.Count - 1,
					nextLegIndex: null);

			return arrival + accessDuration;
		}
	}

	/// <summary>
	/// Total journey duration, consistent with the displayed departure and
	/// arrival (realtime included). Falls back to the provider's planned
	/// duration when a time is missing.
	/// </summary>
	public TimeSpan Duration =>
		Departure is { } start
		&& Arrival is { } end
		&& end >= start
			? end - start
			: PlannedDuration ?? TimeSpan.Zero;

	public TimeSpan? PlannedDuration { get; init; }

	public int TransferCount =>
		Math.Max(
			0,
			Legs.Count(leg => IsPublicTransport(leg)) - 1);

	public bool HasDelay =>
		Legs.Any(
			leg =>
				leg.DepartureDelay is { } departureDelay
					&& departureDelay != TimeSpan.Zero
				||
				leg.ArrivalDelay is { } arrivalDelay
					&& arrivalDelay != TimeSpan.Zero);

	public bool IsCancelled =>
		Legs.Any(leg => leg.IsCancelled);

	public IReadOnlyList<JourneyTransfer> Transfers { get; init; }
		= Array.Empty<JourneyTransfer>();

	/// <summary>
	/// Arrival of the last leg. Walking legs carry no realtime data, so a
	/// walk that follows a delayed ride is moved later by that delay (it keeps
	/// its planned duration). Without this a final walk would show its
	/// timetable arrival even when the ride before it is late.
	/// </summary>
	private DateTimeOffset? FinalLegArrival()
	{
		DateTimeOffset? previousArrival = null;
		DateTimeOffset? arrival = null;

		foreach (JourneyLeg leg in Legs)
		{
			arrival = leg.EffectiveArrival;

			if (leg.Mode == TransitMode.Walk
				&& previousArrival is { } prior
				&& leg.ScheduledDeparture is { } plannedStart
				&& leg.ScheduledArrival is { } plannedEnd
				&& plannedEnd >= plannedStart)
			{
				DateTimeOffset start =
					prior > plannedStart
						? prior
						: plannedStart;

				arrival = start + (plannedEnd - plannedStart);
			}

			previousArrival = arrival;
		}

		return arrival;
	}

	private TimeSpan TerminalTransferDuration(
		int? previousLegIndex,
		int? nextLegIndex)
	{
		if (Transfers.Count == 0)
		{
			return TimeSpan.Zero;
		}

		TimeSpan total = TimeSpan.Zero;

		foreach (JourneyTransfer transfer in Transfers)
		{
			if (transfer.PreviousLegIndex != previousLegIndex
				|| transfer.NextLegIndex != nextLegIndex
				|| transfer.Duration <= TimeSpan.Zero)
			{
				continue;
			}

			total += transfer.Duration;
		}

		return total;
	}

	private static bool IsPublicTransport(JourneyLeg leg) =>
		leg.Mode != TransitMode.Walk;
}