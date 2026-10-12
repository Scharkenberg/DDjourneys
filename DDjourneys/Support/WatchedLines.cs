using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Core.Storage;

namespace DDjourneys.Support;

/// <summary>
/// The watched lines on the device (key <c>watchedlines.list</c>, the pure list is <see cref="WatchedLineList"/>)
/// and the check that makes them real: fetch the route changes of the selected provider, report the watched
/// lines' active disruptions, compare each line's fingerprint with the last stored one (key
/// <c>watchedlines.state</c>, <see cref="WatchedLineStateList"/>) and answer what changed. The state entries
/// of other providers stay; the checked provider's are replaced wholesale. The first check of a line is a
/// baseline, never a change — the passenger saw the current state when they watched the line.
/// </summary>
public static class WatchedLines
{
	private const string ListKey = "watchedlines.list";
	private const string StateKey = "watchedlines.state";

	/// <summary>One check of the union at a time: the tracker loop, the background worker and a page refresh
	/// may land together, and a second answer would only race the first.</summary>
	private static readonly SemaphoreSlim CheckGate = new(1, 1);

	/// <summary>
	/// Raised after the list changed (every successful toggle). The platform layer re-reads <see cref="Load"/>
	/// then and keeps its periodic background work in step (the W1-B4b hook: schedule while the list is
	/// non-empty, cancel when it is empty); nothing platform-specific lives here.
	/// </summary>
	public static event EventHandler? Changed;

	/// <summary>All watched lines, of every provider.</summary>
	public static IReadOnlyList<WatchedLine> Load()
	{
		try
		{
			return WatchedLineList.Parse(Preferences.Default.Get(ListKey, string.Empty));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Reading the watched lines failed: {ex.Message}");

			return [];
		}
	}

	public static bool IsWatched(string providerId, string? lineName) =>
		WatchedLineList.Contains(Load(), providerId, lineName);

	/// <summary>
	/// Watches or forgets the line of one provider (the same-line rule applies). The periodic background check
	/// (the Android WorkManager round of W1-B4b) is scheduled exactly while at least one line is watched; its
	/// wiring re-reads <see cref="Load"/> after every change here (<see cref="Changed"/>, the seam list is in
	/// CLAUDE.md).
	/// </summary>
	/// <returns>Whether the line is watched now.</returns>
	public static bool Toggle(string providerId, string lineName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
		ArgumentException.ThrowIfNullOrWhiteSpace(lineName);

		bool watched = !IsWatched(providerId, lineName);

		try
		{
			Preferences.Default.Set(
				ListKey,
				WatchedLineList.Write(WatchedLineList.With(Load(), providerId, lineName, watched)));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Remembering the watched lines failed: {ex.Message}");

			return !watched;
		}

		Changed?.Invoke(null, EventArgs.Empty);

		return watched;
	}

	/// <summary>
	/// The one check: the union of the stored watched lines of <paramref name="providerId"/> and the ephemeral
	/// lines (the rides of a followed journey under way — watched for this check only, never stored). Nothing
	/// is fetched when the union is empty. Failures propagate to the caller (the worker retries, pages keep
	/// what they show).
	/// </summary>
	public static async Task<WatchedLineCheckResult> CheckAsync(
		NetworkService network,
		string providerId,
		IReadOnlyList<string> ephemeralLines,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(network);
		ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

		IReadOnlyList<WatchedLine> watched = Load();
		IReadOnlyList<string> watchSet =
			LineDisruptions.WatchSet(watched, providerId, ephemeralLines);

		if (watchSet.Count == 0
			|| !await CheckGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
		{
			return new WatchedLineCheckResult([], []);
		}

		try
		{
			DisruptionReport report =
				await network.GetDisruptionsAsync(false, cancellationToken).ConfigureAwait(false);

			return Evaluate(providerId, watchSet, report, DateTimeOffset.UtcNow);
		}
		finally
		{
			CheckGate.Release();
		}
	}

	/// <summary>
	/// The tracker's share of the check: the changed lines among the ephemeral (ride) lines it asked about.
	/// The stored watched lines are part of the same check, so a followed journey keeps the watched set
	/// current as a side effect.
	/// </summary>
	public static async Task<IReadOnlyList<string>> CheckTrackedAsync(
		NetworkService network,
		string providerId,
		IReadOnlyList<string> rideLines,
		CancellationToken cancellationToken = default)
	{
		WatchedLineCheckResult result =
			await CheckAsync(network, providerId, rideLines, cancellationToken).ConfigureAwait(false);

		return
			[.. result.Changed
				.Where(change => change.Kind == LineDisruptionChangeKind.Changed)
				.Select(change => change.LineName)
				.Where(name => rideLines.Any(line => LineCourseService.SameLine(line, name)))];
	}

	private static WatchedLineCheckResult Evaluate(
		string providerId,
		IReadOnlyList<string> watchSet,
		DisruptionReport report,
		DateTimeOffset now)
	{
		IReadOnlyList<LineDisruptionReport> lines =
			LineDisruptions.Report(watchSet, report.Changes, now);

		IReadOnlyList<WatchedLineState> previous = ReadState();

		// The provider's own states are replaced wholesale (unwatched lines' states go); other providers' stay.
		List<WatchedLineState> states =
			[.. previous.Where(state => !string.Equals(state.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))];

		foreach (LineDisruptionReport line in lines)
		{
			states.Add(
				new WatchedLineState(
					providerId,
					BoardFilter.LineKey(line.LineName),
					line.Fingerprint));
		}

		try
		{
			Preferences.Default.Set(StateKey, WatchedLineStateList.Write(states));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Remembering the disruption state of the watched lines failed: {ex.Message}");
		}

		WatchedLineStatus.Adopt(providerId, report);

		return new WatchedLineCheckResult(
			[.. lines.Where(line => line.HasActive)],
			LineDisruptions.Diff(previous, providerId, lines));
	}

	private static IReadOnlyList<WatchedLineState> ReadState()
	{
		try
		{
			return WatchedLineStateList.Parse(Preferences.Default.Get(StateKey, string.Empty));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Reading the disruption state of the watched lines failed: {ex.Message}");

			return [];
		}
	}
}
