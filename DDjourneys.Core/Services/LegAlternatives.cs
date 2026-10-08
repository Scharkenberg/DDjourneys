using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Core.Services;

/// <summary>
/// Swapping one ride of a journey for the previous or next one. The provider's own continuation (VVO
/// <c>tr/prevnextmove</c>) is asked first; what it answers is only used when the ride really moved in the wanted
/// direction. Otherwise the journey is rebuilt from ordinary searches, which every provider can answer: the direct
/// connections between the two stops of the ride are listed, the nearest earlier/later one replaces the ride, and a
/// neighbouring part of the journey that no longer fits is planned again.
/// </summary>
public static class LegAlternatives
{
	/// <summary>Rides closer together than this are the same ride (real-time shifts and rounding).</summary>
	public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(30);

	/// <summary>Time to get from one vehicle to the next when the provider names no walk between them.</summary>
	public static readonly TimeSpan MinimumChange = TimeSpan.FromMinutes(1);

	private const int SearchResults = 8;

	/// <summary>
	/// From journeys the provider returned for "swap ride <paramref name="index"/>": the one whose ride is the nearest
	/// one on the wanted side of the original. Null when none moved (the provider answered with the same ride).
	/// </summary>
	public static Journey? Pick(
		Journey original,
		int index,
		bool previous,
		IEnumerable<Journey> candidates)
	{
		ArgumentNullException.ThrowIfNull(original);
		ArgumentNullException.ThrowIfNull(candidates);

		if (index < 0
			|| index >= original.Legs.Count
			|| PlannedDeparture(original.Legs[index]) is not { } start)
		{
			return null;
		}

		JourneyLeg ride = original.Legs[index];

		Journey? best = null;
		TimeSpan bestDistance = TimeSpan.MaxValue;

		foreach (Journey candidate in candidates)
		{
			foreach (JourneyLeg leg in candidate.Legs.Where(leg => leg.IsRide && SameService(leg, ride) && SameStop(leg.From, ride.From)))
			{
				if (PlannedDeparture(leg) is not { } moved)
				{
					continue;
				}

				TimeSpan distance = previous ? start - moved : moved - start;

				if (distance > Tolerance
					&& distance < bestDistance)
				{
					best = candidate;
					bestDistance = distance;
				}
			}
		}

		return best;
	}

	/// <summary>
	/// The journey with ride <paramref name="index"/> replaced by the previous or next direct connection between the
	/// same two stops, found with <paramref name="search"/> (one provider's ordinary search). Null when there is none, or
	/// when the rest of the journey cannot be fitted to it.
	/// </summary>
	public static async Task<Journey?> ComposeAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		JourneyQuery query,
		Journey journey,
		int index,
		bool previous,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		if (index < 0
			|| index >= journey.Legs.Count
			|| !journey.Legs[index].IsRide
			|| journey.Legs[index].EffectiveDeparture is not { } departure
			|| journey.Legs[index].EffectiveArrival is not { } arrival)
		{
			return null;
		}

		JourneyLeg ride = journey.Legs[index];

		JourneyLeg? swapped =
			await FindRideAsync(search, query, ride, departure, arrival, previous, cancellationToken).ConfigureAwait(false);

		if (swapped is null
			|| swapped.EffectiveDeparture is not { } newDeparture
			|| swapped.EffectiveArrival is not { } newArrival)
		{
			DiagnosticLog.Write($"[Leg alternative] no {(previous ? "earlier" : "later")} direct connection {ride.From.Name} > {ride.To.Name} after {departure:HH:mm}");

			return null;
		}

		DiagnosticLog.Write($"[Leg alternative] ride {ride.Line?.Name} {departure:HH:mm} replaced by {swapped.Line?.Name} {newDeparture:HH:mm}");

		JourneySegment head = JourneySplice.Slice(journey, 0, index);
		JourneySegment own = JourneySplice.Slice(journey, index, index + 1);
		JourneySegment tail = JourneySplice.Slice(journey, index + 1, journey.Legs.Count);
		bool replanned = false;

		// What the swapped ride needs to reach the leg after it (a later ride) or to be reached from the leg before it
		// (an earlier ride): the walk the provider planned between them plus a minute.
		if (!previous
			&& index + 1 < journey.Legs.Count
			&& !Reaches(newArrival, journey, index, index + 1, journey.Legs[index + 1].EffectiveDeparture))
		{
			tail =
				await PlanAsync(
					search,
					query,
					AsLocation(ride.To),
					query.To,
					JourneySearchMode.Departure,
					newArrival + MinimumChange,
					cancellationToken)
				.ConfigureAwait(false);

			replanned = true;
		}
		else if (previous
			&& index > 0
			&& !Reaches(journey.Legs[index - 1].EffectiveArrival, journey, index - 1, index, newDeparture))
		{
			head =
				await PlanAsync(
					search,
					query,
					query.From,
					AsLocation(ride.From),
					JourneySearchMode.Arrival,
					newDeparture - MinimumChange,
					cancellationToken)
				.ConfigureAwait(false);

			// The re-planned beginning ends at this stop by itself; the walk the old one led into the ride is gone.
			own = new([], [.. own.Transfers.Where(transfer => transfer.PreviousLegIndex is not null)]);
			replanned = true;
		}

		if (replanned
			&& (previous ? head : tail).Legs.Count == 0)
		{
			return null;
		}

		// The connections next to the new ride are judged by the timeline's own arithmetic, not by the old pair's flags.
		JourneySegment middle =
			new([swapped], JourneySplice.Renewed(own).Transfers);

		// The walk and wait between the old ride and the leg after it described a pair that is gone, unless that
		// remainder was planned again by the provider (then it carries its own).
		if (!(replanned && !previous))
		{
			tail = JourneySplice.RenewedLeading(tail);
		}

		return JourneySplice.Assemble(journey, [head, middle, tail], keepFares: !replanned);
	}

	private static async Task<JourneyLeg?> FindRideAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		JourneyQuery query,
		JourneyLeg ride,
		DateTimeOffset departure,
		DateTimeOffset arrival,
		bool previous,
		CancellationToken cancellationToken)
	{
		// Direct connections only, between exactly these two stops.
		RoutingPreferences routing =
			query.Routing with
			{
				MaxTransfers = MaxTransfers.None,
				AlternativeStops = false
			};

		var sub = new JourneyQuery
		{
			From = AsLocation(ride.From),
			To = AsLocation(ride.To),
			DateTime = previous ? arrival - Tolerance : departure + Tolerance,
			SearchMode = previous ? JourneySearchMode.Arrival : JourneySearchMode.Departure,
			MaxResults = SearchResults,
			TimeoutSeconds = query.TimeoutSeconds,
			Routing = routing
		};

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var rides = new List<JourneyLeg>();

		// The first answer holds a handful of connections; walking further away from the original time yields more.
		for (int round = 0; round < 3; round++)
		{
			JourneyResult result = await search(sub, cancellationToken).ConfigureAwait(false);

			if (result.Journeys.Count == 0)
			{
				break;
			}

			foreach (Journey found in result.Journeys)
			{
				JourneyLeg[] direct = [.. found.Legs.Where(leg => leg.IsRide)];

				if (direct.Length != 1
					|| !SameStop(direct[0].From, ride.From)
					|| !SameStop(direct[0].To, ride.To)
					|| PlannedDeparture(direct[0]) is not { } start)
				{
					continue;
				}

				if (seen.Add($"{start:O}|{direct[0].Line?.Name}"))
				{
					rides.Add(direct[0]);
				}
			}

			JourneyLeg? chosen = Choose(rides, ride, departure, previous);

			if (chosen is not null)
			{
				return chosen;
			}

			// Nothing on the wanted side yet: look further out, from the far end of what was seen.
			DateTimeOffset? edge =
				previous
					? rides.Select(PlannedDeparture).Min() ?? sub.DateTime
					: rides.Select(PlannedDeparture).Max() ?? sub.DateTime;

			sub = new JourneyQuery
			{
				From = sub.From,
				To = sub.To,
				DateTime = previous ? edge.Value : edge.Value + Tolerance,
				SearchMode = sub.SearchMode,
				MaxResults = SearchResults,
				TimeoutSeconds = query.TimeoutSeconds,
				Routing = routing
			};
		}

		return null;
	}

	/// <summary>The nearest ride on the wanted side of the original; the same line wins over another one at the same stops.</summary>
	private static JourneyLeg? Choose(
		IReadOnlyList<JourneyLeg> rides,
		JourneyLeg original,
		DateTimeOffset departure,
		bool previous)
	{
		IEnumerable<JourneyLeg> onSide =
			rides.Where(
				leg => PlannedDeparture(leg) is { } start
					&& (previous ? start < departure - Tolerance : start > departure + Tolerance));

		JourneyLeg[] same = [.. onSide.Where(leg => SameService(leg, original))];

		IEnumerable<JourneyLeg> pool = same.Length > 0 ? same : onSide;

		return previous
			? pool.OrderByDescending(leg => PlannedDeparture(leg)).FirstOrDefault()
			: pool.OrderBy(leg => PlannedDeparture(leg)).FirstOrDefault();
	}

	/// <summary>
	/// Plans the rest (departing from <paramref name="at"/>) or the beginning (arriving by it) of the journey again and
	/// returns the best connection as a run, or null when there is none.
	/// </summary>
	private static async Task<JourneySegment> PlanAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		JourneyQuery query,
		Location from,
		Location to,
		JourneySearchMode mode,
		DateTimeOffset at,
		CancellationToken cancellationToken)
	{
		var sub = new JourneyQuery
		{
			From = from,
			To = to,
			DateTime = at,
			SearchMode = mode,
			MaxResults = 3,
			TimeoutSeconds = query.TimeoutSeconds,
			Routing = query.Routing
		};

		JourneyResult result = await search(sub, cancellationToken).ConfigureAwait(false);

		IEnumerable<Journey> usable =
			result.Journeys.Where(
				journey => journey.Legs.Count > 0
					&& (mode == JourneySearchMode.Departure
						? JourneyWindow.PlannedStart(journey) >= at - Tolerance
						: JourneyWindow.PlannedEnd(journey) <= at + Tolerance));

		Journey? best =
			mode == JourneySearchMode.Departure
				? usable.OrderBy(journey => JourneyWindow.PlannedEnd(journey)).FirstOrDefault()
				: usable.OrderByDescending(journey => JourneyWindow.PlannedStart(journey)).FirstOrDefault();

		return best is null
			? JourneySegment.Empty
			: JourneySegment.Of(best);
	}

	/// <summary>True when the vehicle that arrives at <paramref name="arrival"/> still reaches the next one (or, for an earlier ride, when the ride before it still reaches this one).</summary>
	private static bool Reaches(
		DateTimeOffset? arrival,
		Journey journey,
		int previousIndex,
		int nextIndex,
		DateTimeOffset? departure)
	{
		if (arrival is not { } from
			|| departure is not { } to)
		{
			// Without times nothing can be shown to be wrong; the timeline draws what it has.
			return true;
		}

		TimeSpan walk =
			journey.Transfers
				.Where(transfer => transfer.PreviousLegIndex == previousIndex && transfer.NextLegIndex == nextIndex)
				.Aggregate(TimeSpan.Zero, (sum, transfer) => sum + transfer.Duration);

		return to >= from + walk + MinimumChange;
	}

	private static Location AsLocation(Station station) =>
		new()
		{
			Id = station.Id,
			ProviderId = station.ProviderId,
			Name = station.Name,
			Place = station.Place,
			Latitude = station.Latitude,
			Longitude = station.Longitude,
			Kind = PlaceKind.Stop
		};

	private static DateTimeOffset? PlannedDeparture(JourneyLeg leg) =>
		leg.ScheduledDeparture is { } planned && planned != default
			? planned
			: leg.EffectiveDeparture;

	/// <summary>The same line: name and kind of vehicle; the direction too when both ride names say one.</summary>
	public static bool SameService(JourneyLeg a, JourneyLeg b) =>
		a.Mode == b.Mode
		&& string.Equals(
			JourneyIdentity.Normalize(a.Line?.Name),
			JourneyIdentity.Normalize(b.Line?.Name),
			StringComparison.Ordinal)
		&& (string.IsNullOrWhiteSpace(a.Line?.Destination)
			|| string.IsNullOrWhiteSpace(b.Line?.Destination)
			|| string.Equals(
				JourneyIdentity.Normalize(a.Line.Destination),
				JourneyIdentity.Normalize(b.Line.Destination),
				StringComparison.Ordinal));

	/// <summary>The same stop: its id, or else its name in the same place.</summary>
	public static bool SameStop(Station a, Station b) =>
		(!string.IsNullOrWhiteSpace(a.Id)
			&& string.Equals(a.Id, b.Id, StringComparison.Ordinal)
			&& (string.IsNullOrWhiteSpace(a.ProviderId)
				|| string.IsNullOrWhiteSpace(b.ProviderId)
				|| string.Equals(a.ProviderId, b.ProviderId, StringComparison.OrdinalIgnoreCase)))
		|| (string.Equals(
				JourneyIdentity.Normalize(a.Name),
				JourneyIdentity.Normalize(b.Name),
				StringComparison.Ordinal)
			&& (string.IsNullOrWhiteSpace(a.Place)
				|| string.IsNullOrWhiteSpace(b.Place)
				|| string.Equals(
					JourneyIdentity.Normalize(a.Place),
					JourneyIdentity.Normalize(b.Place),
					StringComparison.Ordinal)));
}
