using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Services;

/// <summary>
/// Finds the whole run behind a journey leg. A passenger usually opens the vehicle page before the vehicle has reached
/// the boarding stop, and the leg itself only knows boarding to alighting, so a vehicle still on its way to the stop
/// would not fit its course. The departure monitor of the boarding stop knows the run before and after it: the departure
/// that is this leg (same line, same scheduled time) is looked up, and its run, whichever provider gave it, is the course.
/// </summary>
public sealed class LegRunResolver(DepartureService departures)
{
	/// <summary>How far the scheduled time of the departure may be from the leg's.</summary>
	public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(90);

	/// <summary>How far the time of an occurrence may be from the leg's: the pick and the anchor each
	/// allow 90 seconds, and their compounding is this.</summary>
	private static readonly TimeSpan SpanTolerance = TimeSpan.FromSeconds(180);

	/// <returns>The target with the full run, or null when no matching departure or no usable run was found.</returns>
	public async Task<TrackTarget?> ResolveAsync(
		JourneyLeg leg,
		string line,
		int timeoutSeconds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(leg);
		ArgumentNullException.ThrowIfNull(line);

		if (leg.ScheduledDeparture is not { } planned
			|| string.IsNullOrWhiteSpace(leg.From.Id)
			|| !departures.IsAvailable)
		{
			return null;
		}

		var stop =
			new Location
			{
				Id = leg.From.Id,
				ProviderId = leg.From.ProviderId,
				Name = leg.From.Name,
				Place = leg.From.Place,
				Latitude = leg.From.Latitude,
				Longitude = leg.From.Longitude,
				Kind = PlaceKind.Stop
			};

		DepartureBoard board =
			await departures
				.GetDeparturesAsync(
					new DepartureQuery
					{
						Stop = stop,
						Time = planned - TimeSpan.FromMinutes(2),
						Limit = 30,
						TimeoutSeconds = timeoutSeconds
					},
					cancellationToken)
				.ConfigureAwait(false);

		if (Pick(board.Departures, leg, line) is not { } departure)
		{
			return null;
		}

		RunDetail detail =
			await departures
				.GetRunDetailAsync(departure, timeoutSeconds, cancellationToken)
				.ConfigureAwait(false);

		return FromRun(detail.Stops, departure, leg, line, detail.Path);
	}

	/// <summary>
	/// The departure that is the leg: the same line at the same scheduled time. When several fit (two vehicles of a line
	/// within the tolerance), the one going to the leg's destination, then the closest in time.
	/// </summary>
	public static Departure? Pick(IReadOnlyList<Departure> candidates, JourneyLeg leg, string line)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		ArgumentNullException.ThrowIfNull(leg);

		if (leg.ScheduledDeparture is not { } planned)
		{
			return null;
		}

		string wanted = Normalize(line);
		string? destination = leg.Line?.Destination;

		return candidates
			.Where(
				departure => !departure.IsArrival
					&& Normalize(departure.Line.Name) == wanted
					&& (departure.Scheduled - planned).Duration() <= TimeTolerance)
			.OrderBy(departure => SameDestination(departure.Line.Destination, destination) ? 0 : 1)
			.ThenBy(departure => (departure.Scheduled - planned).Duration())
			.FirstOrDefault();
	}

	/// <summary>The target from a run's stops: those with a position, in travel order, with their real-time times.</summary>
	public static TrackTarget? FromRun(
		IReadOnlyList<RunStop> stops,
		Departure departure,
		JourneyLeg leg,
		string line,
		IReadOnlyList<(double Latitude, double Longitude)>? path = null)
	{
		ArgumentNullException.ThrowIfNull(stops);
		ArgumentNullException.ThrowIfNull(departure);
		ArgumentNullException.ThrowIfNull(leg);

		// The points of a stop list: positioned, in travel order, with their times — one projection for
		// the run that is matched and for the whole itinerary that is drawn.
		IReadOnlyList<CoursePoint> PointsOf(IReadOnlyList<RunStop> list) =>
			[.. list
				.Where(
					stop => stop.Station.Latitude is { } lat
						&& stop.Station.Longitude is { } lon
						&& !(lat == 0 && lon == 0))
				.Select(
					stop => new CoursePoint(
						stop.Station.Latitude!.Value,
						stop.Station.Longitude!.Value,
						stop.Effective,
						stop.Station.Name,
						stop.Station.Id))];

		IReadOnlyList<CoursePoint> course =
		PointsOf(RunCourse.Isolate(stops, departure.Scheduled));

		IReadOnlyList<CoursePoint> itinerary =
		PointsOf(stops);

		(int Start, int End)? ride =
		RideSpan(itinerary, leg);

		var target =
			new TrackTarget
			{
				Line = line,
				Mode = leg.Mode,
				Direction = leg.Line?.Destination ?? departure.Line.Destination,
				Course = course,
				Itinerary = itinerary,
				RideStart = ride?.Start ?? -1,
				RideEnd = ride?.End ?? -1,
				Path = path is { Count: > 1 } ? path : null
			};

		return target.IsUsable
			? target
			: null;
	}

	/// <summary>
	/// Where the passenger's ride sits in the itinerary: the boarding stop at the leg's departure time to
	/// the alighting stop after it. Null when the itinerary does not contain the leg's stops.
	/// </summary>
	private static (int Start, int End)? RideSpan(
		IReadOnlyList<CoursePoint> points,
		JourneyLeg leg)
	{
		int board =
			Occurrence(points, leg.From, leg.ScheduledDeparture, 0);

		if (board < 0)
		{
			return null;
		}

		int alight =
			Occurrence(points, leg.To, leg.ScheduledArrival, board + 1);

		return alight > board
			? (board, alight)
			: null;
	}

	/// <summary>
	/// The occurrence of the stop from <paramref name="start"/> whose time is the wanted one — the
	/// anchor's own minute. Without a time to compare, or when none matches, the first occurrence at all
	/// (a loop may pass a stop twice).
	/// </summary>
	private static int Occurrence(
		IReadOnlyList<CoursePoint> points,
		Station stop,
		DateTimeOffset? time,
		int start)
	{
		int first = -1;

		for (int index = Math.Max(0, start); index < points.Count; index++)
		{
			CoursePoint point = points[index];

			if (!SameStop(point, stop))
			{
				continue;
			}

			first = first < 0 ? index : first;

			if (point.Time is { } at
				&& time is { } wanted
				&& Math.Abs((at - wanted).TotalSeconds) <= SpanTolerance.TotalSeconds)
			{
				return index;
			}
		}

		return first;
	}

	/// <summary>By stop id when both carry one (the VVO forms agree), else by name.</summary>
	private static bool SameStop(
		CoursePoint point,
		Station stop) =>
		!string.IsNullOrEmpty(point.Id) && !string.IsNullOrEmpty(stop.Id)
			? string.Equals(point.Id, stop.Id, StringComparison.OrdinalIgnoreCase)
			: string.Equals(point.Name, stop.Name, StringComparison.OrdinalIgnoreCase);

	private static string Normalize(string? text) =>
		string.Concat((text ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

	private static bool SameDestination(string? a, string? b) =>
		!string.IsNullOrWhiteSpace(a)
		&& !string.IsNullOrWhiteSpace(b)
		&& (a.Contains(b, StringComparison.CurrentCultureIgnoreCase)
			|| b.Contains(a, StringComparison.CurrentCultureIgnoreCase));
}
