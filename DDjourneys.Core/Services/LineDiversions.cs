using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>
/// Diversions on the line-course maps (roadmap #17), pure: what the data says about a line whose route is
/// changed, and how a line course shows it.
///
/// Honest scope, from what the data carries: a route change (VVO <c>rc</c>) has a title, a free-text
/// description, validity periods and line ids — it names NO stops and carries NO path geometry. The only
/// machine-readable skipping the data offers is the run window's own stop states: a stop of the run marked
/// cancelled is a stop the vehicle does not serve. So the line-course map marks exactly those stops and
/// shows the disruption's notice card; a diversion path is never drawn (there is no geometry to draw, and
/// stops are never guessed from free text).
/// </summary>
public static class LineDiversions
{
	/// <summary>
	/// The stops of the course the data identifies as skipped: those marked cancelled in the run window the
	/// course was built from. Everything else is served as planned.
	/// </summary>
	public static IReadOnlyList<RunStop> SkippedStops(LineCourse course)
	{
		ArgumentNullException.ThrowIfNull(course);

		return [.. course.Stops.Where(stop => stop.IsCancelled)];
	}

	/// <summary>
	/// The active route changes of the line (the same-line rule, the validity period containing
	/// <paramref name="now"/>): the ones whose notice card the line-course map shows.
	/// </summary>
	public static IReadOnlyList<Disruption> ActiveChangesOf(
		IReadOnlyList<Disruption> changes,
		string? lineName,
		DateTimeOffset now) =>
		[.. changes.Where(change => change.IsActiveAt(now) && LineDisruptions.AffectsLine(change, lineName))];

	/// <summary>
	/// The notice text of one change for the notice card: the title as its own paragraph, then the
	/// description's paragraphs (the card's parser splits them; links inside the text stay tappable).
	/// </summary>
	public static string NoticeText(Disruption change)
	{
		ArgumentNullException.ThrowIfNull(change);

		return string.IsNullOrWhiteSpace(change.Description)
			? change.Title
			: $"{change.Title}\n\n{change.Description}";
	}
}
