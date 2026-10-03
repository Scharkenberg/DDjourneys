using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Core.Services;

/// <summary>
/// Provider-independent windowing of search results: exactly the requested number of journeys,
/// and reliable "earlier" / "later" pages.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here relies on a provider's own paging (VVO's <c>prevnext</c> is undocumented in how it
/// treats the time and repeats connections already shown). Instead the window is moved with plain
/// searches: "later" asks for departures from the last shown journey on, "earlier" for arrivals up
/// to the first shown one. Each round moves a cursor past what it got, so a round that only
/// repeats known journeys still makes progress, and the number of rounds is capped.
/// </para>
/// <para>
/// All comparisons use timetable times (<see cref="PlannedStart"/>, <see cref="PlannedEnd"/>):
/// a delay must neither reorder the list nor let a journey slip into the next page twice.
/// Duplicates are recognised by <see cref="JourneyIdentity"/>, which is also timetable-based.
/// </para>
/// </remarks>
public static class JourneyWindow
{
	/// <summary>Upper bound of searches per fill or page, so a sparse timetable cannot loop.</summary>
	public const int MaxRounds = 4;

	/// <summary>Distance the cursor moves past the last journey of a round.</summary>
	public static readonly TimeSpan Step = TimeSpan.FromMinutes(1);

	/// <summary>Planned start: first planned departure, including a walk before the first vehicle.</summary>
	public static DateTimeOffset? PlannedStart(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		if (journey.Legs.Count == 0)
		{
			return null;
		}

		JourneyLeg first = journey.Legs[0];

		return (first.ScheduledDeparture ?? first.EffectiveDeparture) - journey.AccessDuration;
	}

	/// <summary>Planned end: last planned arrival, including a walk after the last vehicle.</summary>
	public static DateTimeOffset? PlannedEnd(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		if (journey.Legs.Count == 0)
		{
			return null;
		}

		JourneyLeg last = journey.Legs[^1];

		return (last.ScheduledArrival ?? last.EffectiveArrival) + journey.EgressDuration;
	}

	/// <summary>The time a list in this mode is ordered by: departure, or arrival for "arrive by".</summary>
	public static DateTimeOffset? SortKey(Journey journey, JourneySearchMode mode) =>
		mode == JourneySearchMode.Arrival
			? PlannedEnd(journey)
			: PlannedStart(journey);

	/// <summary>Key for duplicate detection; journeys without planned times count as distinct.</summary>
	public static string IdentityOf(Journey journey) =>
		JourneyIdentity.Of(journey)?.Key
		?? $"{PlannedStart(journey)?.ToUnixTimeMilliseconds()}>{PlannedEnd(journey)?.ToUnixTimeMilliseconds()}#"
			+ string.Join("|", journey.Legs.Select(leg => leg.Line?.Name))
			+ "#" + (journey.Id ?? Guid.NewGuid().ToString("N"));

	/// <summary>Orders by the mode's key; journeys without a key go last; equal keys keep their order.</summary>
	public static List<Journey> Order(IEnumerable<Journey> journeys, JourneySearchMode mode) =>
		journeys
			.Select((journey, index) => (journey, index, key: SortKey(journey, mode)))
			.OrderBy(item => item.key is null)
			.ThenBy(item => item.key)
			.ThenBy(item => item.index)
			.Select(item => item.journey)
			.ToList();

	/// <summary>
	/// Brings a first answer to exactly <paramref name="wanted"/> journeys when the timetable allows:
	/// missing ones are added in the direction away from the requested time (later for departures,
	/// earlier for "arrive by"), surplus ones are cut from the far end.
	/// </summary>
	public static async Task<IReadOnlyList<Journey>> FillAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		JourneyQuery query,
		IReadOnlyList<Journey> first,
		int wanted,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(first);

		wanted = Math.Max(1, wanted);

		List<Journey> all = Distinct(first);

		if (all.Count > 0 && all.Count < wanted)
		{
			bool later = query.SearchMode == JourneySearchMode.Departure;

			PageResult more =
				await PageAsync(
					search,
					query,
					all,
					previous: !later,
					wanted - all.Count,
					cancellationToken)
				.ConfigureAwait(false);

			// A failure while topping up is not a failure of the search: what was found stays.
			all.AddRange(more.Journeys);
		}

		return Trim(Order(all, query.SearchMode), query.SearchMode, wanted);
	}

	/// <summary>
	/// The next <paramref name="wanted"/> journeys before or after <paramref name="shown"/>,
	/// none of them already shown, ordered like the list.
	/// </summary>
	public static async Task<PageResult> PageAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		JourneyQuery query,
		IReadOnlyList<Journey> shown,
		bool previous,
		int wanted,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(shown);

		wanted = Math.Max(1, wanted);

		List<Journey> ordered = Order(shown, query.SearchMode);

		// The boundary of the list in its own order, and the cursor in the time the search speaks.
		Journey? edge = previous ? ordered.FirstOrDefault() : ordered.LastOrDefault();

		DateTimeOffset? boundary = edge is null ? query.DateTime : SortKey(edge, query.SearchMode);

		DateTimeOffset? cursor =
			edge is null
				? query.DateTime
				: previous
					? PlannedEnd(edge)
					: PlannedStart(edge);

		if (boundary is null || cursor is null)
		{
			return new PageResult([], null);
		}

		var seen = new HashSet<string>(shown.Select(IdentityOf), StringComparer.Ordinal);
		var found = new List<Journey>();
		JourneyResult? failure = null;

		for (int round = 0; round < MaxRounds && found.Count < wanted; round++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Earlier: what arrives up to the first journey; later: what departs from the last one on.
			JourneyQuery step =
				Copy(
					query,
					cursor.Value,
					previous ? JourneySearchMode.Arrival : JourneySearchMode.Departure);

			JourneyResult result =
				await search(step, cancellationToken).ConfigureAwait(false);

			if (result.Outcome is JourneyOutcome.Failed or JourneyOutcome.NotSuitable)
			{
				failure = result;
				break;
			}

			if (result.Journeys.Count == 0)
			{
				break;
			}

			foreach (Journey journey in result.Journeys)
			{
				if (SortKey(journey, query.SearchMode) is not { } key
					|| (previous ? key > boundary.Value : key < boundary.Value)
					|| !seen.Add(IdentityOf(journey)))
				{
					continue;
				}

				found.Add(journey);
			}

			DateTimeOffset? next =
				previous
					? result.Journeys.Select(PlannedEnd).Where(time => time is not null).Min() - Step
					: result.Journeys.Select(PlannedStart).Where(time => time is not null).Max() + Step;

			// Always move on, even if the provider only repeated itself.
			cursor =
				next is null
					? (previous ? cursor - Step : cursor + Step)
					: previous
						? Earliest(next.Value, cursor.Value - Step)
						: Latest(next.Value, cursor.Value + Step);
		}

		List<Journey> page = Order(found, query.SearchMode);

		// Keep the journeys closest to the list: the latest of the earlier ones, the first of the later ones.
		page =
			previous
				? page.Skip(Math.Max(0, page.Count - wanted)).ToList()
				: page.Take(wanted).ToList();

		return new PageResult(page, page.Count == 0 ? failure : null);
	}

	private static IReadOnlyList<Journey> Trim(
		List<Journey> ordered,
		JourneySearchMode mode,
		int wanted)
	{
		if (ordered.Count <= wanted)
		{
			return ordered;
		}

		// Departures: the first ones after the requested time. Arrive by: the last ones before it.
		return mode == JourneySearchMode.Arrival
			? ordered.Skip(ordered.Count - wanted).ToList()
			: ordered.Take(wanted).ToList();
	}

	private static List<Journey> Distinct(IEnumerable<Journey> journeys)
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);

		return journeys.Where(journey => seen.Add(IdentityOf(journey))).ToList();
	}

	private static JourneyQuery Copy(
		JourneyQuery query,
		DateTimeOffset time,
		JourneySearchMode mode) =>
		new()
		{
			From = query.From,
			To = query.To,
			DateTime = time,
			SearchMode = mode,
			MaxResults = query.MaxResults,
			TimeoutSeconds = query.TimeoutSeconds,
			Routing = query.Routing
		};

	private static DateTimeOffset Earliest(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

	private static DateTimeOffset Latest(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}

/// <summary>A page of journeys; <see cref="Failure"/> is set only when nothing could be found because a search failed.</summary>
public sealed record PageResult(
	IReadOnlyList<Journey> Journeys,
	JourneyResult? Failure);
