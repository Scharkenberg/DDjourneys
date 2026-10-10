using System.Text.RegularExpressions;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>
/// The course of a whole line for the map ("where does this line actually go?"): one run of the line,
/// taken from the departure board, carries the sliding window that covers the line in its direction. The
/// run detail's own data is enough - no new interface; the departures entry can also load both directions
/// with one extra request.
/// </summary>
public sealed partial class LineCourseService(DepartureService departures)
{
	/// <summary>The run of the departure, as the window covers it; null when nothing of it can be drawn.</summary>
	public async Task<LineCourse?> ForDepartureAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(departure);

		RunDetail detail =
			await departures.GetRunDetailAsync(departure, timeoutSeconds, cancellationToken);

		return FromDetail(departure, detail);
	}

	/// <summary>
	/// The next run of the line from the stop's board: the first same-line departure, and when a second
	/// direction can be told apart (its direction letter, or differing termini), that one as well - one
	/// extra request.
	/// </summary>
	public async Task<IReadOnlyList<LineCourse>> BothDirectionsAsync(
		TransitLine line,
		Station stop,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(line);
		ArgumentNullException.ThrowIfNull(stop);

		DepartureBoard board =
			await departures.GetDeparturesAsync(
				new DepartureQuery
				{
					Stop = ToLocation(stop),
					Time = null,
					Limit = 30,
					TimeoutSeconds = timeoutSeconds
				},
				cancellationToken);

		List<Departure> sameLine =
			[.. board.Departures.Where(candidate => SameLine(candidate.Line.Name, line.Name))];

		if (sameLine.Count == 0)
		{
			return [];
		}

		Departure first = sameLine.FirstOrDefault(candidate => !candidate.IsCancelled) ?? sameLine[0];
		string? firstDirection = DirectionOf(first);

		Departure? second =
			sameLine.FirstOrDefault(candidate =>
				!ReferenceEquals(candidate, first)
					&& !candidate.IsCancelled
					&& DirectionOf(candidate) is { } candidateDirection
					&& firstDirection is { } known
					&& candidateDirection != known)
			?? sameLine.FirstOrDefault(candidate =>
				!ReferenceEquals(candidate, first)
					&& !candidate.IsCancelled
					&& !string.Equals(candidate.Line.Destination, first.Line.Destination, StringComparison.OrdinalIgnoreCase));

		LineCourse? firstCourse =
			await ForDepartureAsync(first, timeoutSeconds, cancellationToken);

		if (firstCourse is null)
		{
			return [];
		}

		if (second is null)
		{
			return [firstCourse];
		}

		LineCourse? secondCourse =
			await ForDepartureAsync(second, timeoutSeconds, cancellationToken);

		return secondCourse is null
			? [firstCourse]
			: [firstCourse, secondCourse];
	}

	/// <summary>The next run of the line from the stop's board (the first of the directions).</summary>
	public async Task<LineCourse?> ForLineAtStopAsync(
		TransitLine line,
		Station stop,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<LineCourse> courses =
			await BothDirectionsAsync(line, stop, timeoutSeconds, cancellationToken);

		return courses.Count > 0 ? courses[0] : null;
	}

	/// <summary>What one run detail says about the whole line; null when there is nothing to draw.</summary>
	public static LineCourse? FromDetail(Departure departure, RunDetail detail)
	{
		IReadOnlyList<RunStop> stops = detail.Stops;

		if (stops.Count == 0)
		{
			return null;
		}

		bool positioned =
			stops.Any(stop => stop.Station.Latitude is not null && stop.Station.Longitude is not null);
		bool path = detail.Path is { Count: > 1 };

		if (!positioned && !path)
		{
			return null;
		}

		return new LineCourse(
			departure.Line.Name,
			departure.Line.Mode,
			DirectionOf(departure),
			Deduplicate(stops),
			detail.Path,
			stops[0].Station.Name,
			stops[^1].Station.Name)
		{
			Vehicle = detail.Vehicle
		};
	}

	/// <summary>
	/// The window chains several journeys of the line, so the same station appears once per chained journey:
	/// keep the first occurrence of each station. Without an id, the rounded position and the name stand in.
	/// </summary>
	internal static IReadOnlyList<RunStop> Deduplicate(IReadOnlyList<RunStop> stops)
	{
		if (stops.Count < 2)
		{
			return stops;
		}

		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		List<RunStop> kept = new(stops.Count);

		foreach (RunStop stop in stops)
		{
			if (seen.Add(KeyOf(stop.Station)))
			{
				kept.Add(stop);
			}
		}

		return kept;
	}

	private static string KeyOf(Station station)
	{
		if (!string.IsNullOrWhiteSpace(station.Id))
		{
			return station.Id;
		}

		return station.Latitude is { } latitude && station.Longitude is { } longitude
			? $"{station.Name.Trim()}|{latitude:F5}|{longitude:F5}"
			: station.Name.Trim();
	}

	/// <summary>
	/// The direction letter: from a stateless id when the provider data carries one (voe:11003: :H:j26),
	/// else whatever the line says about its direction (the VVO dlid).
	/// </summary>
	internal static string? DirectionOf(Departure departure)
	{
		if (departure.ProviderData is string stateless)
		{
			Match letter = StatelessDirection().Match(stateless);

			if (letter.Success)
			{
				return letter.Groups[1].Value;
			}
		}

		return string.IsNullOrWhiteSpace(departure.Line.DirectionId)
			? null
			: departure.Line.DirectionId;
	}

	/// <summary>Line names with or without spaces are the same line ("S 1" and "S1").</summary>
	internal static bool SameLine(string? one, string? other)
	{
		if (one is null || other is null)
		{
			return false;
		}

		return string.Equals(
			Normalize(one),
			Normalize(other),
			StringComparison.OrdinalIgnoreCase);

		static string Normalize(string name) =>
			name.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
	}

	private static Location ToLocation(Station stop) =>
		new()
		{
			Id = stop.Id,
			ProviderId = stop.ProviderId,
			Name = stop.Name,
			Place = stop.Place,
			Latitude = stop.Latitude,
			Longitude = stop.Longitude,
			Kind = PlaceKind.Stop
		};

	/// <summary>The direction letter of a stateless id: voe:11003: :H:j26.</summary>
	[GeneratedRegex(@"^voe:\d+:[^:]*:([A-Za-z]):")]
	private static partial Regex StatelessDirection();
}
