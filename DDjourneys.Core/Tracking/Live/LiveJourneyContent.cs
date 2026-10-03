using System.Globalization;

namespace DDjourneys.Core.Tracking.Live;

/// <summary>
/// Everything the single live presentation of a followed journey shows, independent of how a
/// platform renders it (Android live notification, a widget, an in-app banner, ...).
/// </summary>
/// <param name="PlanId">The followed journey; empty for the generic "monitoring" state.</param>
/// <param name="Segments">Relative lengths of the journey's segments (rides and walks).</param>
/// <param name="Individual">Per segment: walking or other individual transport.</param>
/// <param name="Position">Progress along the summed <paramref name="Segments"/>.</param>
/// <param name="When">The moment the next event happens (countdown target), if any.</param>
/// <param name="PositionAt">The moment <paramref name="Position"/> was true (the start of the current ride or walk), so a renderer can advance the bar with the clock; null means "now".</param>
public sealed record LiveJourneyContent(
	string PlanId,
	string Title,
	string Text,
	string? SubText,
	string? ShortText,
	IReadOnlyList<int> Segments,
	IReadOnlyList<bool> Individual,
	int Position,
	TrackingPhase Phase,
	bool Ongoing,
	DateTimeOffset? When = null,
	DateTimeOffset? PositionAt = null)
{
	/// <summary>Records compare lists by reference; this key compares what is displayed.</summary>
	public string Key =>
		string.Join(
			'|',
			PlanId,
			Title,
			Text,
			SubText,
			ShortText,
			Position.ToString(CultureInfo.InvariantCulture),
			Phase,
			Ongoing,
			When?.ToUnixTimeSeconds(),
			string.Join(',', Segments));
}

public enum JourneyAlertKind
{
	Start,
	Change,
	Problem,
	Arrived
}

/// <summary>One event worth interrupting the user for (start, change, problem, arrival).</summary>
public sealed record JourneyAlert(
	string PlanId,
	JourneyAlertKind Kind,
	string Title,
	string Text);
