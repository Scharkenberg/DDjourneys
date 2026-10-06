namespace DDjourneys.Core.Models;

/// <summary>One point of a course: where a vehicle should be, and when.</summary>
public sealed record CoursePoint(
	double Latitude,
	double Longitude,
	DateTimeOffset? Time,
	string? Name = null);


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
