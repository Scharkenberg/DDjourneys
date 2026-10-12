namespace DDjourneys.Core.Models;

/// <summary>
/// The course of a whole line: the sliding window of one of its runs covers the line in that direction,
/// chained journey by journey. The stops are de-duplicated (the window repeats a station once per chained
/// journey); the termini are the first and last stop of the window.
/// </summary>
public sealed record LineCourse(
	string LineName,
	TransitMode Mode,
	string? Direction,
	IReadOnlyList<RunStop> Stops,
	IReadOnlyList<(double Latitude, double Longitude)> Path,
	string? FirstTerminus,
	string? LastTerminus)
{
	/// <summary>Where the provider last saw a vehicle of the run, when it says so.</summary>
	public GeoPosition? Vehicle { get; init; }
}
