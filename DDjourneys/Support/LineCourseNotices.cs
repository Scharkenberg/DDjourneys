using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;

namespace DDjourneys.Support;

/// <summary>
/// The diversions of a line-course map (roadmap #17): the active route changes of one line, fetched once per
/// map opening, as the note the map page shows beside the course. The pure half of the logic is
/// <see cref="LineDiversions"/> (Core): what is active for the line, which stops are not served and how the
/// note reads. Honest scope: a route change (VVO <c>rc</c>) names no stops and carries no path geometry, so
/// there is never a diversion path to draw — the note is all the data has, and the map's own stop states mark
/// the skipped stops (see <see cref="MapScenes"/>).
/// </summary>
public static class LineCourseNotices
{
	/// <summary>
	/// The note of the line's active route changes (their titles and descriptions, one after the other), or
	/// null when the line has none. A failed fetch answers null: the map opens without the note, never without
	/// the course.
	/// </summary>
	public static async Task<string?> OfLineAsync(
			NetworkService network,
			string? lineName,
			CancellationToken cancellationToken = default)
	{
			ArgumentNullException.ThrowIfNull(network);

			if (string.IsNullOrWhiteSpace(lineName))
			{
					return null;
			}

			try
			{
					DisruptionReport report =
							await network.GetDisruptionsAsync(false, cancellationToken);

					IReadOnlyList<Core.Models.Disruption> changes =
							LineDiversions.ActiveChangesOf(report.Changes, lineName, DateTimeOffset.UtcNow);

					return changes.Count == 0
							? null
							: string.Join(
									"\n\n",
									changes.Select(LineDiversions.NoticeText));
			}
			catch (OperationCanceledException)
			{
					throw;
			}
			catch (Exception ex)
			{
					// The note is a nicety: the course does not depend on it, and a failed fetch says nothing.
					DiagnosticLog.Write($"[Line course] the diversions of {lineName} failed: {ex.Message}");

					return null;
			}
	}
}
