using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Core.Services;

/// <summary>
/// "Via" for any start and destination. A provider is asked with the stop-over first; what it answers is checked: a
/// journey counts only when it really passes the stop (a stop of one of its rides, a transfer, or a walk past it).
/// When the provider ignores the stop-over, fails on it, or has no journey that passes it, the journeys are built from two
/// ordinary searches (start to stop-over, stop-over to destination), which every provider answers whatever kind of
/// place the start and the destination are.
/// </summary>
public static class ViaRouting
{
	/// <summary>Time to change vehicles at the stop-over when the journey stays there at all.</summary>
	public static readonly TimeSpan Stay = TimeSpan.FromMinutes(1);

	/// <summary>A stop closer to the stop-over than this is the stop-over (platforms of one stop lie apart).</summary>
	public const double SameStopMeters = 120;

	private const int Candidates = 4;

	/// <summary>True when the journey goes through the place.</summary>
	public static bool Passes(Journey journey, Location via)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(via);

		if (Matches(journey.From, via)
			|| Matches(journey.To, via))
		{
			return true;
		}

		foreach (JourneyLeg leg in journey.Legs)
		{
			if (Matches(leg.From, via)
				|| Matches(leg.To, via)
				|| leg.Stops.Any(stop => Matches(stop.Station, via)))
			{
				return true;
			}
		}

		return journey.Transfers.Any(
			transfer => (transfer.Location is { } place && Matches(place, via))
				|| Matches(transfer.From, via)
				|| Matches(transfer.To, via));
	}

	/// <summary>
	/// Journeys that pass <paramref name="query"/>'s stop-over, built from two searches with
	/// <paramref name="search"/> (one provider's ordinary search, which is asked without a stop-over).
	/// </summary>
	public static async Task<IReadOnlyList<Journey>> ComposeAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentNullException.ThrowIfNull(query);

		if (query.Via is not { } via)
		{
			return [];
		}

		bool arrival = query.SearchMode == JourneySearchMode.Arrival;
		int wanted = Math.Clamp(query.MaxResults, 1, Candidates);

		// The part that the requested time fixes is searched first (the way to the stop-over when leaving at a time, the way
		// from it when arriving by one); the other part is fitted to each of its answers.
		JourneyQuery first =
			arrival
				? Sub(query, via, query.To, JourneySearchMode.Arrival, query.DateTime, wanted)
				: Sub(query, query.From, via, JourneySearchMode.Departure, query.DateTime, wanted);

		JourneyResult primary = await search(first, cancellationToken).ConfigureAwait(false);

		List<Journey> anchors =
			[.. JourneyWindow
				.Order(primary.Journeys, query.SearchMode)
				.Where(journey => journey.Legs.Count > 0)];

		anchors =
			arrival
				? [.. anchors.Skip(Math.Max(0, anchors.Count - wanted))]
				: [.. anchors.Take(wanted)];

		if (anchors.Count == 0)
		{
			DiagnosticLog.Write($"[Via] no journey {(arrival ? "from" : "to")} the stop-over {via.Name}");

			return [];
		}

		Journey?[] joined =
			await Task.WhenAll(
				anchors.Select(anchor => JoinAsync(search, query, via, anchor, arrival, cancellationToken)))
			.ConfigureAwait(false);

		List<Journey> result =
			JourneyWindow.Order(
				joined.OfType<Journey>().DistinctBy(JourneyWindow.IdentityOf),
				query.SearchMode);

		DiagnosticLog.Write($"[Via] {result.Count} journey(s) built through {via.Name} from {anchors.Count} first part(s)");

		return result;
	}

	private static async Task<Journey?> JoinAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		JourneyQuery query,
		Location via,
		Journey anchor,
		bool arrival,
		CancellationToken cancellationToken)
	{
		if (arrival)
		{
			// anchor: stop-over -> destination; fit the beginning to arrive at the stop-over in time.
			if (JourneyWindow.PlannedStart(anchor) is not { } departs)
			{
				return null;
			}

			JourneyResult head =
				await search(
					Sub(query, query.From, via, JourneySearchMode.Arrival, departs - Stay, 3),
					cancellationToken)
				.ConfigureAwait(false);

			Journey? best =
				head.Journeys
					.Where(journey => journey.Legs.Count > 0 && JourneyWindow.PlannedEnd(journey) <= departs - Stay + LegAlternatives.Tolerance)
					.OrderByDescending(journey => JourneyWindow.PlannedStart(journey))
					.FirstOrDefault();

			return best is null ? null : Join(best, anchor);
		}

		// anchor: start -> stop-over; fit the rest to leave the stop-over after arriving.
		if (JourneyWindow.PlannedEnd(anchor) is not { } arrives)
		{
			return null;
		}

		JourneyResult tail =
			await search(
				Sub(query, via, query.To, JourneySearchMode.Departure, arrives + Stay, 3),
				cancellationToken)
			.ConfigureAwait(false);

		Journey? next =
			tail.Journeys
				.Where(journey => journey.Legs.Count > 0 && JourneyWindow.PlannedStart(journey) >= arrives + Stay - LegAlternatives.Tolerance)
				.OrderBy(journey => JourneyWindow.PlannedEnd(journey))
				.FirstOrDefault();

		return next is null ? null : Join(anchor, next);
	}

	private static Journey Join(Journey head, Journey tail) =>
		JourneySplice.Assemble(
			head,
			[JourneySegment.Of(head), JourneySegment.Of(tail)],
			keepFares: false,
			notices: [.. head.Notices.Concat(tail.Notices).Distinct()],
			destination: tail.Destination);

	private static JourneyQuery Sub(
		JourneyQuery query,
		Location from,
		Location to,
		JourneySearchMode mode,
		DateTimeOffset time,
		int results) =>
		new()
		{
			From = from,
			To = to,
			DateTime = time,
			SearchMode = mode,
			MaxResults = results,
			TimeoutSeconds = query.TimeoutSeconds,
			Routing = query.Routing
		};

	private static bool Matches(Station? station, Location via)
	{
		if (station is null)
		{
			return false;
		}

		if (!string.IsNullOrWhiteSpace(station.Id)
			&& string.Equals(station.Id, via.Id, StringComparison.Ordinal)
			&& (string.IsNullOrWhiteSpace(station.ProviderId)
				|| string.IsNullOrWhiteSpace(via.ProviderId)
				|| string.Equals(station.ProviderId, via.ProviderId, StringComparison.OrdinalIgnoreCase)))
		{
			return true;
		}

		if (string.Equals(
				JourneyIdentity.Normalize(station.Name),
				JourneyIdentity.Normalize(via.Name),
				StringComparison.Ordinal)
			&& (string.IsNullOrWhiteSpace(station.Place)
				|| string.IsNullOrWhiteSpace(via.Place)
				|| string.Equals(
					JourneyIdentity.Normalize(station.Place),
					JourneyIdentity.Normalize(via.Place),
					StringComparison.Ordinal)))
		{
			return true;
		}

		return station.Latitude is { } la
			&& station.Longitude is { } lo
			&& via.Latitude is { } vla
			&& via.Longitude is { } vlo
			&& GeoMath.DistanceMeters(la, lo, vla, vlo) <= SameStopMeters;
	}
}
