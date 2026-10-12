using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>
/// The next departures of one stop, or of several stops around a position: the ONE path the "next
/// departures around" widget and the departures page's nearby rows share (the widget keeps its own row
/// shaping on top of it, <c>WidgetSnapshots</c>). A stop's board is asked with room to spare — a line
/// filter keeps only some of the answer — a few stops at a time, and a stop that fails answers empty
/// instead of failing the whole pass.
/// </summary>
public static class NearbyBoards
{
	/// <summary>Rows kept beyond what is shown: departures that have left by the time the widget is drawn again.</summary>
	public const int Spare = 2;

	/// <summary>Stops asked at the same time (the provider is polite to its own servers).</summary>
	public const int AtATime = 3;

	/// <summary>How long after its time a departure still counts as "next" in a nearby row's preview.</summary>
	private static readonly TimeSpan GoneAfter = TimeSpan.FromMinutes(1);

	/// <summary>
	/// The next departures of one stop: the provider is asked for more than is kept when a line filter will
	/// keep only some of the answer, then the filter is applied client-side and the rows are cut to
	/// <paramref name="keep"/> (see <see cref="Keep"/>).
	/// </summary>
	public static async Task<IReadOnlyList<Departure>> NextOfAsync(
		DepartureService departures,
		Location stop,
		bool arrival,
		int keep,
		IReadOnlyList<string>? lines,
		ModeFilter modes,
		int timeoutSeconds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(departures);
		ArgumentNullException.ThrowIfNull(stop);

		DepartureBoard board =
			await departures
				.GetDeparturesAsync(
					new DepartureQuery
					{
						Stop = stop,
						IsArrival = arrival,
						Limit = keep * (lines is { Count: > 0 } ? 3 : 1),
						Modes = modes,
						TimeoutSeconds = timeoutSeconds
					},
					cancellationToken)
				.ConfigureAwait(false);

		return Keep(board.Departures, keep, lines);
	}

	/// <summary>The client-side half of <see cref="NextOfAsync"/>: the line filter (when one is set) and the cut.</summary>
	public static IReadOnlyList<Departure> Keep(
		IReadOnlyList<Departure> board,
		int keep,
		IReadOnlyList<string>? lines)
	{
		ArgumentNullException.ThrowIfNull(board);

		if (lines is not { Count: > 0 })
		{
			return [.. board.Take(keep)];
		}

		return
			[.. board
				.Where(departure => lines.Contains(departure.Line.Name, StringComparer.OrdinalIgnoreCase))
				.Take(keep)];
	}

	/// <summary>
	/// One board per stop, in the order the stops were given: a few stops at a time
	/// (<see cref="AtATime"/>), and a stop that fails answers empty (the pass as a whole still succeeds;
	/// only cancellation ends it).
	/// </summary>
	public static async Task<IReadOnlyList<Departure>[]> LoadAsync(
		DepartureService departures,
		IReadOnlyList<Location> stops,
		bool arrival,
		int keep,
		IReadOnlyList<string>? lines,
		ModeFilter modes,
		int timeoutSeconds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(departures);

		var boards = new IReadOnlyList<Departure>[stops.Count];

		if (stops.Count == 0)
		{
			return boards;
		}

		using var gate = new SemaphoreSlim(AtATime);

		await Task.WhenAll(
			stops.Select(
				async (stop, index) =>
				{
					await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

					try
					{
						boards[index] =
							await NextOfAsync(departures, stop, arrival, keep, lines, modes, timeoutSeconds, cancellationToken)
								.ConfigureAwait(false);
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						DiagnosticLog.Write($"[Boards] departures of {stop.Name} failed: {ex.Message}");

						boards[index] = [];
					}
					finally
					{
						gate.Release();
					}
				})).ConfigureAwait(false);

		return boards;
	}

	/// <summary>
	/// What a nearby row previews: the next <paramref name="count"/> departures that really leave (not
	/// cancelled, not already gone), in the order the board gave them.
	/// </summary>
	public static IReadOnlyList<Departure> Previews(
		IReadOnlyList<Departure> board,
		int count,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(board);

		return
			[.. board
				.Where(departure => !departure.IsCancelled && now - departure.Effective < GoneAfter)
				.Take(Math.Max(0, count))];
	}
}
