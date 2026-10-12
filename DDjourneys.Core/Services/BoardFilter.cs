using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>
/// The client-side filter of a fetched departure board: which rows are shown when the passenger picked
/// lines and/or wants cancelled ones. Pure logic over the board the page already has — nothing is asked
/// of the provider again, and without a pick the board is shown exactly as it came.
/// </summary>
public static class BoardFilter
{
	/// <summary>
	/// One key per line: names that differ only in spaces or case are the same line ("S 1", "s1") — the
	/// same rule the line course uses to find its line on a board.
	/// </summary>
	public static string LineKey(string? name) =>
		name is null
			? string.Empty
			: name.Replace(" ", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();

	/// <summary>
	/// Keeps the departures of <paramref name="lines"/> (all of them when none is picked, matched by
	/// <see cref="LineKey"/>) and drops cancelled ones unless <paramref name="showCancelled"/> asks for them.
	/// </summary>
	public static IReadOnlyList<Departure> Apply(
		IReadOnlyList<Departure> board,
		IReadOnlyCollection<string>? lines,
		bool showCancelled)
	{
		ArgumentNullException.ThrowIfNull(board);

		HashSet<string>? picked = lines is { Count: > 0 }
			? [.. lines.Select(LineKey)]
			: null;

		if (picked is null && showCancelled)
		{
			return board;
		}

		return
			(List<Departure>)
			[.. board.Where(departure =>
				(picked is null || picked.Contains(LineKey(departure.Line.Name)))
				&& (showCancelled || !departure.IsCancelled))];
	}
}
