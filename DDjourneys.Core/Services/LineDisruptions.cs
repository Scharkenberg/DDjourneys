using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;

namespace DDjourneys.Core.Services;

/// <summary>What one watched line currently has: its fingerprint and the disruptions that are active now.</summary>
public sealed record LineDisruptionReport(
	string LineName,
	string Fingerprint,
	IReadOnlyList<Disruption> Active)
{
	public bool HasActive =>
		Active.Count > 0;
}

public enum LineDisruptionChangeKind
{
	/// <summary>The line was checked for the first time (no stored fingerprint): this is the baseline, not a change.</summary>
	FirstSeen,

	/// <summary>The fingerprint differs from the last check: disruptions were added, removed or re-published.</summary>
	Changed
}

/// <summary>One line whose disruption state moved on since the last check.</summary>
public sealed record LineDisruptionChange(
	string LineName,
	LineDisruptionChangeKind Kind,
	IReadOnlyList<Disruption> Active);

/// <summary>What a check of the watched lines answered: the lines with active disruptions and the ones that changed.</summary>
public sealed record WatchedLineCheckResult(
	IReadOnlyList<LineDisruptionReport> Affected,
	IReadOnlyList<LineDisruptionChange> Changed);

/// <summary>
/// The disruption state of watched lines, pure: which disruptions of a set of changes are active for which
/// line, the fingerprint that summarizes them, how that differs from the last stored state, and what the union
/// of stored watched lines and ephemeral lines (a followed journey under way) is. Nothing here fetches or stores.
/// </summary>
public static class LineDisruptions
{
	/// <summary>
	/// The report of every named line: the disruptions that are active now and name the line (the same-line
	/// rule of <see cref="LineCourseService.SameLine"/> — a disruption of "S 1" is a disruption of "s1").
	/// Lines without any active disruption are reported too (with their empty fingerprint): a line that goes
	/// quiet is a change like any other.
	/// </summary>
	public static IReadOnlyList<LineDisruptionReport> Report(
		IReadOnlyList<string> lineNames,
		IReadOnlyList<Disruption> changes,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(lineNames);
		ArgumentNullException.ThrowIfNull(changes);

		List<LineDisruptionReport> reports = [];

		foreach (string name in lineNames)
		{
			if (string.IsNullOrWhiteSpace(name)
				|| reports.Any(report => LineCourseService.SameLine(report.LineName, name)))
			{
				continue;
			}

			List<Disruption> active =
				[.. changes.Where(change => change.IsActiveAt(now) && AffectsLine(change, name))];

			reports.Add(new LineDisruptionReport(name.Trim(), Fingerprint(active), active));
		}

		return reports;
	}

	/// <summary>
	/// The fingerprint of a line's active disruptions: a stable hash over the change ids and their publish
	/// stamps, sorted, so neither the provider's order nor a re-published text with a new stamp slips
	/// through. FNV-1a (deterministic, unlike <c>string.GetHashCode</c>) as sixteen hex digits.
	/// </summary>
	public static string Fingerprint(IReadOnlyList<Disruption> active)
	{
		ArgumentNullException.ThrowIfNull(active);

		string joined = string.Join(
			';',
			active
				.Select(change => $"{change.Id}|{change.Published?.ToUnixTimeSeconds() ?? 0}")
				.OrderBy(entry => entry, StringComparer.Ordinal));

		// FNV-1a over the joined entries (the same stable hash the alert ids of the live notification use).
		ulong hash = 14695981039346656037;

		foreach (char c in joined)
		{
			hash = (hash ^ c) * 1099511628211;
		}

		return hash.ToString("X16", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// How the current reports differ from the stored states of <paramref name="providerId"/>: a line with no
	/// stored state is a baseline (<see cref="LineDisruptionChangeKind.FirstSeen"/> — the passenger just
	/// started watching; the current state is not news), a line whose fingerprint differs has changed, and a
	/// line whose fingerprint is the same produces nothing. States of lines that are no longer watched are not
	/// reported either: forgetting a line is not disruption news.
	/// </summary>
	public static IReadOnlyList<LineDisruptionChange> Diff(
		IReadOnlyList<WatchedLineState> previous,
		string providerId,
		IReadOnlyList<LineDisruptionReport> current)
	{
		ArgumentNullException.ThrowIfNull(previous);
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentNullException.ThrowIfNull(current);

		List<LineDisruptionChange> changes = [];

		foreach (LineDisruptionReport report in current)
		{
			WatchedLineState? state = previous.FirstOrDefault(candidate =>
				string.Equals(candidate.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(candidate.Line, BoardFilter.LineKey(report.LineName), StringComparison.Ordinal));

			if (state is null)
			{
				changes.Add(new LineDisruptionChange(report.LineName, LineDisruptionChangeKind.FirstSeen, report.Active));

				continue;
			}

			if (!string.Equals(state.Fingerprint, report.Fingerprint, StringComparison.Ordinal))
			{
				changes.Add(new LineDisruptionChange(report.LineName, LineDisruptionChangeKind.Changed, report.Active));
			}
		}

		return changes;
	}

	/// <summary>
	/// The union of the stored watched lines of one provider and the ephemeral lines (the rides of a followed
	/// journey under way — watched for the check, never stored): normalized once, without doubles, ordered.
	/// </summary>
	public static IReadOnlyList<string> WatchSet(
		IReadOnlyList<WatchedLine> watched,
		string providerId,
		IReadOnlyList<string> ephemeralLines)
	{
		ArgumentNullException.ThrowIfNull(watched);
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentNullException.ThrowIfNull(ephemeralLines);

		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (string name in watched
			.Where(line => string.Equals(line.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
			.Select(line => line.Name)
			.Concat(ephemeralLines))
		{
			if (name is { Length: > 0 })
			{
				seen.Add(BoardFilter.LineKey(name));
			}
		}

		return [.. seen.OrderBy(name => name, StringComparer.Ordinal)];
	}

	/// <summary>
	/// The ride lines of a journey: the ephemeral watch candidates while it is under way. A check's union
	/// (<see cref="WatchSet"/>) takes them next to the stored watched lines, so a followed journey's lines are
	/// watched for that check without ever being stored. Walks and transfers name no line.
	/// </summary>
	public static IReadOnlyList<string> RideLines(Journey journey)
	{
			ArgumentNullException.ThrowIfNull(journey);

			return
					[.. journey.Rides
							.Select(leg => leg.Line?.Name)
							.Where(name => name is { Length: > 0 })
							.Select(name => name!)
							.Distinct(StringComparer.CurrentCultureIgnoreCase)];
	}

	/// <summary>Whether the change is active and names this line (the same-line rule).</summary>
	public static bool AffectsLine(Disruption change, string? lineName) =>
		lineName is { Length: > 0 }
			&& change.Lines.Any(line => LineCourseService.SameLine(line.Name, lineName));
}
