using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Support;

/// <summary>
/// The session's view of the watched lines: what the planner banner and the settings row show. Fed by every
/// answer the app already has (the disruptions page, a line-course map) and by every check
/// (<see cref="WatchedLines.CheckAsync"/> — the tracker loop while a journey is under way, the Android
/// background worker); a page that wants fresh data asks <see cref="RefreshAsync"/>, which stays quiet for
/// <see cref="Quiet"/> after any answer, so nothing fetches on every appearing page.
///
/// The banner's dismissal is per session and per disruption: dismissing remembers the ids it hid, and the
/// banner returns when a disruption appears that was not dismissed. A restarted app shows it again.
/// </summary>
public static class WatchedLineStatus
{
	/// <summary>How long an answer stays fresh for the banner and the settings row.</summary>
	private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(10);

	private static readonly Lock Gate = new();

	private static string _providerId = string.Empty;
	private static IReadOnlyList<LineDisruptionReport> _affected = [];
	private static DateTimeOffset _answered = DateTimeOffset.MinValue;

	// Disruption ids the passenger sent away this session (the banner stays gone until a new one appears).
	private static readonly HashSet<string> Dismissed = new(StringComparer.Ordinal);

	/// <summary>The provider of the current answer (an answer of a provider that is no longer selected is not shown).</summary>
	public static string ProviderId
	{
		get
		{
			lock (Gate)
			{
				return _providerId;
			}
		}
	}

	/// <summary>The watched lines with active disruptions, as the last answer reported them.</summary>
	public static IReadOnlyList<LineDisruptionReport> Affected
	{
		get
		{
			lock (Gate)
			{
				return _affected;
			}
		}
	}

	/// <summary>Whether the planner banner has anything to say for this provider.</summary>
	public static bool HasBanner(string providerId)
	{
		lock (Gate)
		{
			return string.Equals(_providerId, providerId, StringComparison.OrdinalIgnoreCase)
				&& VisibleAffected().Count > 0;
		}
	}

	/// <summary>The lines the banner names for this provider (affected, not dismissed).</summary>
	public static IReadOnlyList<LineDisruptionReport> BannerLines(string providerId)
	{
		lock (Gate)
		{
			return string.Equals(_providerId, providerId, StringComparison.OrdinalIgnoreCase)
				? VisibleAffected()
				: [];
		}
	}

	/// <summary>Sends the banner away for this session: every currently affected disruption is dismissed.</summary>
	public static void DismissBanner()
	{
		lock (Gate)
		{
			foreach (LineDisruptionReport line in _affected)
			{
				foreach (Disruption change in line.Active)
				{
					Dismissed.Add(change.Id);
				}
			}
		}
	}

	/// <summary>
	/// Adopts an answer the app already has (the disruptions page, a line-course map): computes the watched
	/// lines' state from it — nothing is fetched — and marks the session as answered.
	/// </summary>
	public static void Adopt(string providerId, DisruptionReport report)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentNullException.ThrowIfNull(report);

		try
		{
			IReadOnlyList<string> watched =
				[.. WatchedLines.Load()
					.Where(line => string.Equals(line.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
					.Select(line => line.Name)];

			IReadOnlyList<LineDisruptionReport> affected =
				LineDisruptions.Report(watched, report.Changes, DateTimeOffset.UtcNow)
					.Where(line => line.HasActive)
					.ToArray();

			lock (Gate)
			{
				_providerId = providerId;
				_affected = affected;
				_answered = DateTimeOffset.UtcNow;
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Adopting the watched lines' state failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Asks for a fresh answer when the last one is older than <see cref="Quiet"/> (a page appearing). Never
	/// throws: a failed check keeps the previous answer and can be asked for again.
	/// </summary>
	public static async Task RefreshAsync(
		NetworkService network,
		string providerId,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(network);

		if (!Due())
		{
			return;
		}

		try
		{
			await WatchedLines.CheckAsync(network, providerId, [], cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Checking the watched lines failed: {ex.Message}");
		}
	}

	private static bool Due()
	{
		lock (Gate)
		{
			return DateTimeOffset.UtcNow - _answered >= Quiet;
		}
	}

	private static List<LineDisruptionReport> VisibleAffected() =>
		[.. _affected.Where(line => line.Active.Any(change => !Dismissed.Contains(change.Id)))];
}
