using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>What the feeder did with the query.</summary>
public enum BikeFeederOutcome
{
	/// <summary>Feeder journeys were composed: the bike leg rides in front of the transit part.</summary>
	Composed,

	/// <summary>The query does not ask for the feeder (toggle off, the start is a stop, no coordinates).</summary>
	NotApplicable,

	/// <summary>No stop within the radius had a bike track (BRouter unreachable, or no candidate answered).</summary>
	NoBikeRoute,

	/// <summary>No transit journey left the boarding stop; the ordinary search answers instead.</summary>
	NoTransit
}

/// <summary>The feeder answer: the composed journeys, and why there are none when there are none.</summary>
public sealed record BikeFeederResult(
	IReadOnlyList<Journey> Journeys,
	BikeFeederOutcome Outcome,
	string? BoardingStop);

/// <summary>
/// "Bike to the stop": the leg in front of a journey that the passenger rides by bike, from the searched
/// start (an address, a point of interest, a position) to a boarding stop within the chosen radius, planned
/// as a real track by the BRouter service. The stop is chosen to avoid pushing: among the candidates whose
/// ride is comparable, the one with the least ascent. The transit part is an ordinary provider search from
/// the boarding stop; both are joined on the same substrate as a stop-over journey
/// (<see cref="JourneySplice.Assemble"/>).
/// </summary>
public static class BikeFeeder
{
	/// <summary>How many stops around the start are asked for a bike track (the nearest first).</summary>
	public const int MaxCandidates = 6;

	/// <summary>
	/// The time at the stop after arriving by bike, for parking and locking it before boarding. The transit
	/// part is searched so that its first vehicle leaves after this buffer.
	/// </summary>
	public static readonly TimeSpan BoardingBuffer = TimeSpan.FromMinutes(2);

	/// <summary>
	/// What makes two rides to different stops comparable: at most this much slower than the fastest one.
	/// Only among comparable rides does the least ascent win (a stop that needs ten more minutes is not the
	/// same journey, however flat).
	/// </summary>
	public static readonly TimeSpan ComparableRide = TimeSpan.FromMinutes(5);

	/// <summary>The radius preference's range, kilometres.</summary>
	public const int MinRadiusKm = 1;

	/// <summary>The radius preference's range, kilometres.</summary>
	public const int MaxRadiusKm = 5;

	/// <summary>
	/// The notice the ordinary journeys carry when the feeder could not run: a stable marker the app words in
	/// the user's language when it shows it (the core library has no strings); the technical view shows it as
	/// it is.
	/// </summary>
	public const string UnavailableNotice = "dd: bike-feeder-unavailable";

	/// <summary>The radius around the start within which stops are considered, metres: the preference clamped to its range.</summary>
	public static int RadiusMeters(RoutingPreferences routing) =>
		Math.Clamp(routing.BikeFeederRadiusKm, MinRadiusKm, MaxRadiusKm) * 1000;

	/// <summary>
	/// True when the query asks for the feeder and it can apply: the start is an address, a point of interest
	/// or a position with coordinates — a stop needs no bike leg, the passenger boards there. A stop-over is
	/// composed separately (see <c>JourneyService</c>): the two special modes are not stacked on one query.
	/// </summary>
	public static bool Applies(JourneyQuery query) =>
		query.Routing.BikeFeeder
		&& !query.From.IsStation
		&& query.From.Latitude is not null
		&& query.From.Longitude is not null;

	/// <summary>True when the journey starts with the feeder leg (a bike leg in front of the first ride).</summary>
	public static bool IsFeeder(Journey journey) =>
		journey.Legs.Count > 0
		&& journey.Legs[0].Mode == TransitMode.Bike;

	/// <summary>
	/// The feeder journeys of one provider: the boarding stop is chosen from the stops within the radius (the
	/// <paramref name="stopsNear"/> delegate, nearest first, at most <see cref="MaxCandidates"/>), each with
	/// its bike track (<paramref name="route"/>, cached by the client); the transit part is the provider's own
	/// search (<paramref name="search"/>) from the boarding stop, asked without the feeder, and every answer
	/// gets the bike leg fitted in front of it. Without a boarding stop or without transit the feeder does not
	/// run; the caller answers with the ordinary search.
	/// </summary>
	public static async Task<BikeFeederResult> ComposeAsync(
		Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search,
		Func<double, double, int, CancellationToken, Task<IReadOnlyList<NearbyStop>>> stopsNear,
		Func<double, double, double, double, CancellationToken, Task<BikeRoute?>> route,
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(search);
		ArgumentNullException.ThrowIfNull(stopsNear);
		ArgumentNullException.ThrowIfNull(route);
		ArgumentNullException.ThrowIfNull(query);

		if (!Applies(query))
		{
			return new BikeFeederResult([], BikeFeederOutcome.NotApplicable, null);
		}

		Location from = query.From;
		int radius = RadiusMeters(query.Routing);

		IReadOnlyList<NearbyStop> stops;

		try
		{
			stops = await stopsNear(
				from.Latitude!.Value,
				from.Longitude!.Value,
				radius,
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Bike] the stops around the start failed ({ex.Message}); no feeder");

			return new BikeFeederResult([], BikeFeederOutcome.NoBikeRoute, null);
		}

		NearbyStop[] asked =
			[.. stops
				.Where(stop => stop.Stop.Latitude is not null && stop.Stop.Longitude is not null)
				.OrderBy(stop => stop.DistanceMeters)
				.Take(MaxCandidates)];

		if (asked.Length == 0)
		{
			DiagnosticLog.Write($"[Bike] no stop within {radius} m of '{from.Name}'; no feeder");

			return new BikeFeederResult([], BikeFeederOutcome.NoBikeRoute, null);
		}

		// The tracks are asked in parallel: offline, every candidate would otherwise hold the search for its
		// own timeout.
		(NearbyStop Stop, BikeRoute? Route)[] tracks =
			await Task.WhenAll(
				asked.Select(stop => AskAsync(route, from, stop, cancellationToken)))
			.ConfigureAwait(false);

		(Location Stop, BikeRoute Route)[] reachable =
			[.. tracks.Where(track => track.Route is not null).Select(track => (track.Stop.Stop, track.Route!))];

		if (reachable.Length == 0)
		{
			DiagnosticLog.Write($"[Bike] no bike track from '{from.Name}' to any of {asked.Length} stop(s); no feeder");

			return new BikeFeederResult([], BikeFeederOutcome.NoBikeRoute, null);
		}

		(Location Stop, BikeRoute Route) boarding = Board(reachable);

		bool arrival = query.SearchMode == JourneySearchMode.Arrival;

		// Departure: the first vehicle may leave once the bike is there and parked. Arrival: the transit part
		// has to reach the destination by the asked time, and the bike is fitted in front of whatever it found.
		JourneyQuery transit =
			new()
			{
				From = boarding.Stop,
				To = query.To,
				DateTime = arrival
					? query.DateTime
					: query.DateTime + boarding.Route.Duration + BoardingBuffer,
				SearchMode = query.SearchMode,
				MaxResults = Math.Max(1, query.MaxResults),
				TimeoutSeconds = query.TimeoutSeconds,
				Routing = query.Routing with { BikeFeeder = false }
			};

		JourneyResult part = await search(transit, cancellationToken).ConfigureAwait(false);

		if (part.Outcome != JourneyOutcome.Found || part.Journeys.Count == 0)
		{
			DiagnosticLog.Write($"[Bike] no transit from the boarding stop '{boarding.Stop.Name}'; the ordinary search answers");

			return new BikeFeederResult([], BikeFeederOutcome.NoTransit, boarding.Stop.Name);
		}

		var composed = new List<Journey>(part.Journeys.Count);

		foreach (Journey journey in part.Journeys)
		{
			if (Compose(query, from, boarding.Stop, boarding.Route, journey) is { } withBike)
			{
				composed.Add(withBike);
			}
		}

		if (composed.Count == 0)
		{
			DiagnosticLog.Write($"[Bike] {part.Journeys.Count} transit journey(s) from '{boarding.Stop.Name}', none fits the bike ride; the ordinary search answers");

			return new BikeFeederResult([], BikeFeederOutcome.NoTransit, boarding.Stop.Name);
		}

		DiagnosticLog.Write(
			$"[Bike] {composed.Count} journey(s) with a bike leg to '{boarding.Stop.Name}' ({boarding.Route.Duration.TotalMinutes:0.#} min, +{boarding.Route.AscentMeters} m up), {reachable.Length} of {asked.Length} stop(s) reachable");

		return new BikeFeederResult(composed, BikeFeederOutcome.Composed, boarding.Stop.Name);
	}

	/// <summary>
	/// The boarding stop: among the candidates whose ride is comparable (at most
	/// <see cref="ComparableRide"/> slower than the fastest one) the one with the least ascent — the one
	/// without pushing. Ties go to the faster ride, then to the shorter one, then to the stop's id; the
	/// rule is deterministic.
	/// </summary>
	public static (Location Stop, BikeRoute Route) Board(
		IReadOnlyList<(Location Stop, BikeRoute Route)> candidates)
	{
		ArgumentNullException.ThrowIfNull(candidates);

		if (candidates.Count == 0)
		{
			throw new ArgumentException("The boarding choice needs at least one candidate.", nameof(candidates));
		}

		TimeSpan fastest = candidates.Min(candidate => candidate.Route.Duration);

		return candidates
			.Where(candidate => candidate.Route.Duration <= fastest + ComparableRide)
			.OrderBy(candidate => candidate.Route.AscentMeters)
			.ThenBy(candidate => candidate.Route.Duration)
			.ThenBy(candidate => candidate.Route.DistanceMeters)
			.ThenBy(candidate => candidate.Stop.Id, StringComparer.Ordinal)
			.First();
	}

	/// <summary>
	/// The same journey with one notice added. The copy keeps everything a provider journey carries — its
	/// session, its fares, its raw data: a hint the app adds while planning must not degrade the journey it
	/// explains.
	/// </summary>
	public static Journey WithNotice(Journey journey, string notice)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(notice);

		return new Journey
		{
			Legs = journey.Legs,
			From = journey.From,
			To = journey.To,
			Origin = journey.Origin,
			Destination = journey.Destination,
			Id = journey.Id,
			ProviderId = journey.ProviderId,
			Context = journey.Context,
			ProviderData = journey.ProviderData,
			Parts = journey.Parts,
			Notices = [.. journey.Notices.Where(item => !string.Equals(item, notice, StringComparison.Ordinal)), notice],
			Fares = journey.Fares,
			Transfers = journey.Transfers,
			PlannedDuration = journey.PlannedDuration
		};
	}

	/// <summary>
	/// One journey with the bike leg in front: the leg arrives at the boarding stop one boarding buffer before
	/// the transit part leaves, and starts one bike ride earlier. A "leave at" search never starts before the
	/// asked time — a transit journey that would need that is dropped, not shown as an impossible connection.
	/// An "arrive by" search fits the bike in front of whatever the transit part offered.
	/// </summary>
	private static Journey? Compose(
		JourneyQuery query,
		Location from,
		Location boarding,
		BikeRoute track,
		Journey transit)
	{
		if (transit.Legs.Count == 0
			|| JourneyWindow.PlannedStart(transit) is not { } boards)
		{
			return null;
		}

		DateTimeOffset arrivesAtStop = boards - BoardingBuffer;
		DateTimeOffset leaves = arrivesAtStop - track.Duration;

		if (query.SearchMode == JourneySearchMode.Departure && leaves < query.DateTime)
		{
			return null;
		}

		JourneyLeg bike =
			new()
			{
				Mode = TransitMode.Bike,
				From = StationOf(from),
				To = StationOf(boarding),
				Path = track.Path,
				ScheduledDeparture = leaves,
				ScheduledArrival = arrivesAtStop,
				DistanceMeters = track.DistanceMeters,
				AscentMeters = track.AscentMeters,
				DescentMeters = track.DescentMeters
			};

		// The transit part keeps its fares (the bike adds none) and stays a part of its own, so following the
		// journey follows the vehicles; the bike leg itself is not a ride (see TransitModeExtensions).
		return JourneySplice.Assemble(
			transit,
			[JourneySegment.Of(bike), JourneySegment.Of(transit)],
			keepFares: true,
			origin: StationOf(from),
			parts: [transit]);
	}

	private static async Task<(NearbyStop Stop, BikeRoute? Route)> AskAsync(
		Func<double, double, double, double, CancellationToken, Task<BikeRoute?>> route,
		Location from,
		NearbyStop stop,
		CancellationToken cancellationToken)
	{
		try
		{
			BikeRoute? track =
				await route(
					from.Latitude!.Value,
					from.Longitude!.Value,
					stop.Stop.Latitude!.Value,
					stop.Stop.Longitude!.Value,
					cancellationToken).ConfigureAwait(false);

			return (stop, track);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Bike] the track to '{stop.Stop.Name}' failed ({ex.Message})");

			return (stop, null);
		}
	}

	private static Station StationOf(Location place) =>
		new()
		{
			Id = place.Id ?? string.Empty,
			ProviderId = place.ProviderId,
			Name = place.Name,
			Place = place.Place,
			Latitude = place.Latitude,
			Longitude = place.Longitude
		};
}
