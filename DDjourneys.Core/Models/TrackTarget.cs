namespace DDjourneys.Core.Models;

/// <summary>One point of a course: where a vehicle should be, and when.</summary>
public sealed record CoursePoint(
	double Latitude,
	double Longitude,
	DateTimeOffset? Time,
	string? Name = null,
	string? Id = null);


/// <summary>
/// The one run a passenger wants to follow: the line, the direction and the stops with their (real-time) times,
/// taken from the journey leg or the departure that was tapped. <see cref="RunMatcher"/> uses it to pick that
/// run out of all vehicles of the line.
/// </summary>
public sealed class TrackTarget
{
	/// <summary>The line as shown to the passenger ("7").</summary>
	public required string Line { get; init; }

	public TransitMode Mode { get; init; }

	public string? Direction { get; init; }

	/// <summary>Stops in travel order; at least two with a position and a time to be matchable.</summary>
	public required IReadOnlyList<CoursePoint> Course { get; init; }

	/// <summary>
	/// The vehicle's whole itinerary around the run (the sliding window of chained journeys), when it is
	/// known: the map shows it with the stops of the followed ride in the colour of its line and the rest
	/// quiet grey. Built positioned-only, like <see cref="Course"/>, so the ride's span indexes it as is.
	/// </summary>
	public IReadOnlyList<CoursePoint>? Itinerary { get; init; }

	/// <summary>
	/// Where the followed ride sits in <see cref="Itinerary"/>: the boarding stop to the alighting stop,
	/// or the run's own span when a departure was followed. -1 when the itinerary does not contain them.
	/// </summary>
	public int RideStart { get; init; } = -1;

	/// <summary>-1 with <see cref="RideStart"/>; the alighting stop otherwise.</summary>
	public int RideEnd { get; init; } = -1;

	/// <summary>
	/// The real line of the route (the provider's map data) when it has one; the map draws it instead of
	/// straight lines between the stops. Empty or null when the provider has none.
	/// </summary>
	public IReadOnlyList<(double Latitude, double Longitude)>? Path { get; init; }

	/// <summary>The line number the live positions use, when the line is a plain number.</summary>
	public int? LineNumber =>
		int.TryParse(
			Line.Trim(),
			System.Globalization.NumberStyles.None,
			System.Globalization.CultureInfo.InvariantCulture,
			out int number)
			? number
			: null;

	public bool IsUsable =>
		LineNumber is not null
		&& Course.Count(point => point.Time is not null) >= 2;
}
