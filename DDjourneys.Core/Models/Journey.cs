namespace DDjourneys.Core.Models;

public sealed class Journey
{
	public required IReadOnlyList<JourneyLeg> Legs { get; init; }

	public required Station From { get; init; }

	public required Station To { get; init; }

	/// <summary>
	/// The place the passenger starts from, when it differs from the first vehicle stop
	/// (a walk leads to the first stop). Null when the provider did not say.
	/// </summary>
	public Station? Origin { get; init; }

	/// <summary>The place the passenger is going to, when it differs from the last vehicle stop.</summary>
	public Station? Destination { get; init; }

	/// <summary>Walking (or other transfer) time before the first vehicle.</summary>
	public TimeSpan AccessDuration =>
		Legs.Count == 0
			? TimeSpan.Zero
			: TerminalTransferDuration(
				previousLegIndex: null,
				nextLegIndex: 0);

	/// <summary>Walking (or other transfer) time after the last vehicle.</summary>
	public TimeSpan EgressDuration =>
		Legs.Count == 0
			? TimeSpan.Zero
			: TerminalTransferDuration(
				previousLegIndex: Legs.Count - 1,
				nextLegIndex: null);

	public string? Id { get; init; }

	/// <summary>Stable id of the provider this journey came from (see <c>ProviderInfo.Id</c>).</summary>
	public string ProviderId { get; init; } = string.Empty;

	public string? Context { get; init; }

	/// <summary>
	/// The provider's own object this journey was mapped from (for diagnostics such as the expert view);
	/// never interpreted by the app logic.
	/// </summary>
	public object? ProviderData { get; init; }

	public IReadOnlyList<string> Notices { get; init; }
		= [];

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

	/// <summary>The scheduled public-transport legs; everything else is part of a transfer.</summary>
	public IEnumerable<JourneyLeg> Rides =>
		Legs.Where(leg => leg.IsRide);

	/// <summary>Changes between rides: rides minus one, however many walks lie between them.</summary>
	public int TransferCount =>
		Math.Max(
			0,
			Rides.Count() - 1);

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

	/// <summary>
	/// Why the journey cannot be travelled as planned (a ride cancelled, a stop where one gets on or off is
	/// not served, a connection that cannot be reached); null when nothing stands in the way.
	/// </summary>
	public JourneyBlock? Block => JourneyFeasibility.Assess(this);

	/// <summary>The journey cannot take place as planned. Broader than <see cref="IsCancelled"/>.</summary>
	public bool IsImpossible => Block is not null;

	/// <summary>Tickets and prices the provider quotes for the whole journey.</summary>
	public IReadOnlyList<JourneyFare> Fares { get; init; }
		= [];


	public IReadOnlyList<JourneyTransfer> Transfers { get; init; }
		= [];

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
}