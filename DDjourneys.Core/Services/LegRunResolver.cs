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

		return FromRun(detail.Stops, departure, leg, line);
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
		string line)
	{
		ArgumentNullException.ThrowIfNull(stops);
		ArgumentNullException.ThrowIfNull(departure);
		ArgumentNullException.ThrowIfNull(leg);

		IReadOnlyList<CoursePoint> course =
			RunCourse
				.Isolate(stops, departure.Scheduled)
				.Where(
					stop => stop.Station.Latitude is { } lat
						&& stop.Station.Longitude is { } lon
						&& !(lat == 0 && lon == 0))
				.Select(
					stop => new CoursePoint(
						stop.Station.Latitude!.Value,
						stop.Station.Longitude!.Value,
						stop.Effective,
						stop.Station.Name))
				.ToList();

		var target =
			new TrackTarget
			{
				Line = line,
				Mode = leg.Mode,
				Direction = leg.Line?.Destination ?? departure.Line.Destination,
				Course = course
			};

		return target.IsUsable
			? target
			: null;
	}

	private static string Normalize(string? text) =>
		string.Concat((text ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();

	private static bool SameDestination(string? a, string? b) =>
		!string.IsNullOrWhiteSpace(a)
		&& !string.IsNullOrWhiteSpace(b)
		&& (a.Contains(b, StringComparison.CurrentCultureIgnoreCase)
			|| b.Contains(a, StringComparison.CurrentCultureIgnoreCase));
}
