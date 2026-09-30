using DDjourneys.Core.Models;

namespace DDjourneys.Support;

public abstract record TimelineItem;

public sealed record RideItem(JourneyLeg Leg) : TimelineItem;

public sealed record WalkItem(
	JourneyLeg Leg,
	DateTimeOffset? EffectiveDeparture = null,
	DateTimeOffset? EffectiveArrival = null) : TimelineItem;

/// <summary>
/// The point between two legs: an interchange, or a provider message
/// attached to it (for example a guaranteed connection).
/// </summary>
public sealed record BoundaryItem(
	Station At,
	TimeSpan Wait,
	IReadOnlyList<string> Notes,
	bool ShowWait,
	bool Endangered = false,
	TimeSpan? WalkTime = null) : TimelineItem;

/// <summary>
/// Turns a provider-neutral Journey into a flat, display-ready list.
/// This is the only place that interprets provider quirks for the UI.
/// </summary>
public static class TimelineBuilder
{
	public static IReadOnlyList<TimelineItem> Build(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		var items = new List<TimelineItem>(journey.Legs.Count * 2);

		// A provider transfer belongs to exactly one boundary, even if the route
		// passes the same station twice.
		var used = new HashSet<JourneyTransfer>();
		DateTimeOffset? previousEffectiveArrival = null;

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			JourneyLeg leg = journey.Legs[i];
			DateTimeOffset? effectiveArrival = leg.EffectiveArrival;
			if (leg.Mode == TransitMode.Walk)
			{
				DateTimeOffset? departure = leg.EffectiveDeparture;
				DateTimeOffset? arrival = leg.EffectiveArrival;
				if (previousEffectiveArrival is { } priorArrival
					&& leg.ScheduledDeparture is { } scheduledDeparture
					&& leg.ScheduledArrival is { } scheduledArrival
					&& scheduledArrival >= scheduledDeparture)
				{
					TimeSpan walkDuration = scheduledArrival - scheduledDeparture;
					departure = priorArrival > scheduledDeparture ? priorArrival : scheduledDeparture;
					arrival = departure + walkDuration;
				}
				effectiveArrival = arrival;

				items.Add(new WalkItem(leg, departure, arrival));
			}
			else
			{
				items.Add(new RideItem(leg));
			}

			if (i == journey.Legs.Count - 1)
			{
				// VVO models endpoint footpaths as transfers without a following
				// transit leg. Preserve that final movement in the timeline.
				JourneyTransfer? finalWalk = journey.Transfers
					.Where(t => !used.Contains(t)
						&& t.Kind == TransferKind.Walk
						&& t.Duration > TimeSpan.Zero
						&& Matches(t.Location.Id, leg.To.Id, journey.To.Id))
					.OrderByDescending(t => t.Duration)
					.FirstOrDefault();

				if (finalWalk is not null)
				{
					used.Add(finalWalk);
					items.Add(new BoundaryItem(
						journey.To,
						TimeSpan.Zero,
						finalWalk.Notices,
						ShowWait: false,
						Endangered: false,
						WalkTime: finalWalk.Duration));
				}

				break;
			}

			previousEffectiveArrival = effectiveArrival;

			JourneyLeg next = journey.Legs[i + 1];

			// ASSUMPTION: a provider message belongs to the boundary whose
			// station ID it carries. Verify against captured VVO responses.
			JourneyTransfer[] transfers = journey.Transfers
				.Where(t => !used.Contains(t) && Matches(t.Location.Id, leg.To.Id, next.From.Id))
				.ToArray();

			foreach (JourneyTransfer transfer in transfers)
			{
				used.Add(transfer);
			}

			string[] notes = transfers
				.SelectMany(t => t.Notices)
				.Where(n => !string.IsNullOrWhiteSpace(n))
				.Distinct()
				.ToArray();

			bool isInterchange = leg.Mode != TransitMode.Walk && next.Mode != TransitMode.Walk;

			if (isInterchange || notes.Length > 0)
			{
				// Use effective times (realtime if available, otherwise scheduled) to calculate wait
				// This ensures delays propagate through to footpath/walking times
				TimeSpan wait =
					next.EffectiveDeparture is { } departs && leg.EffectiveArrival is { } arrives && departs >= arrives
						? departs - arrives
						: TimeSpan.Zero;

				// For footpath legs, calculate walk time from the transfer duration
				// This ensures delays affect the footpath timing
				TimeSpan? walk = transfers
					.Where(t => t.Kind == TransferKind.Walk && t.Duration > TimeSpan.Zero)
					.Select(t => (TimeSpan?)t.Duration)
					.Max();

				items.Add(new BoundaryItem(
					leg.To,
					wait,
					notes,
					ShowWait: isInterchange,
					Endangered: transfers.Any(t => !t.IsGuaranteed),
					WalkTime: walk));
			}
		}

		return items;
	}

	/// <summary>Gets the effective end of a final walking transfer.</summary>
	public static DateTimeOffset? FinalWalkArrival(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		IReadOnlyList<TimelineItem> items = Build(journey);
		if (items.LastOrDefault() is not BoundaryItem { WalkTime: { } walkTime })
		{
			return items.LastOrDefault() is WalkItem walk
				? walk.EffectiveArrival ?? walk.Leg.EffectiveArrival
				: journey.Arrival;
		}

		DateTimeOffset? arrival = items
			.Take(items.Count - 1)
			.LastOrDefault(item => item is RideItem or WalkItem) switch
			{
				WalkItem walk => walk.EffectiveArrival ?? walk.Leg.EffectiveArrival,
				RideItem ride => ride.Leg.EffectiveArrival,
				_ => null
			};

		return arrival is { } time ? time + walkTime : null;
	}

	private static bool Matches(string? transferId, string? fromId, string? toId) =>
		!string.IsNullOrWhiteSpace(transferId)
		&& (string.Equals(transferId, fromId, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(transferId, toId, StringComparison.OrdinalIgnoreCase));
}
