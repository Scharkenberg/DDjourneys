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
	IReadOnlyList<string> Notes) : TimelineItem;

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
			string[] notes = journey.Transfers
				.Where(t => t.Location.Id == leg.To.Id
						 || t.Location.Id == next.From.Id)
				.SelectMany(t => t.Notices)
				.Distinct()
				.ToArray();

			bool isInterchange = !isWalk && next.Mode != TransitMode.Walk;

			if (isInterchange || notes.Length > 0)
			{
				TimeSpan wait = (next.EffectiveDeparture ?? default) - (leg.EffectiveArrival ?? default);

				items.Add(new BoundaryItem(
					leg.To,
					wait < TimeSpan.Zero ? TimeSpan.Zero : wait,
					notes));
			}
		}

		return items;
	}
}
