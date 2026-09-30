using DDjourneys.Core.Models;

namespace DDjourneys.Support;

public abstract record TimelineItem;

public sealed record RideItem(JourneyLeg Leg) : TimelineItem;

public sealed record WalkItem(JourneyLeg Leg) : TimelineItem;

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

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			JourneyLeg leg = journey.Legs[i];
			bool isWalk = leg.Mode == TransitMode.Walk;

			items.Add(isWalk ? new WalkItem(leg) : new RideItem(leg));

			if (i == journey.Legs.Count - 1)
			{
				break;
			}

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

			bool isInterchange = !isWalk && next.Mode != TransitMode.Walk;

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

				// If this is a walking connection (footpath) between legs, use the actual
				// effective times to calculate the walk duration, accounting for delays
				if (isWalk && next.Mode == TransitMode.Walk && walk is null)
				{
					// For consecutive walking legs, calculate walk time from effective times
					if (leg.EffectiveArrival is { } legArrives && next.EffectiveDeparture is { } nextDeparts)
					{
						walk = nextDeparts - legArrives;
					}
				}

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

	private static bool Matches(string? transferId, string? fromId, string? toId) =>
		!string.IsNullOrWhiteSpace(transferId)
		&& (string.Equals(transferId, fromId, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(transferId, toId, StringComparison.OrdinalIgnoreCase));
}
