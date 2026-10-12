using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The client-side board filter of the departures page (B1): which rows a fetched board shows when lines
/// are picked and cancelled ones are hidden — multi-select, only-this-line, the cancelled toggle, and the
/// line-name normalisation.
/// </summary>
public class BoardFilterTests
{
	private static readonly DateTimeOffset At = new(2026, 10, 5, 7, 20, 0, TimeSpan.FromHours(2));

	private static Departure Dep(
		string line,
		int minutes,
		bool cancelled = false,
		string destination = "Bühlau") =>
		new()
		{
			Id = $"{line}-{minutes}",
			StopId = "33000028",
			Line = new TransitLine
			{
				Name = line,
				Mode = TransitMode.Tram,
				Destination = destination
			},
			Scheduled = At.AddMinutes(minutes),
			State = cancelled ? DepartureState.Cancelled : DepartureState.InTime
		};

	[Fact]
	public void Without_a_pick_and_with_cancelled_shown_the_board_is_returned_as_it_is()
	{
		Departure[] board = [Dep("11", 0), Dep("66", 5, cancelled: true)];

		Assert.Same(board, BoardFilter.Apply(board, [], showCancelled: true));
	}

	[Fact]
	public void By_default_cancelled_rows_are_hidden()
	{
		IReadOnlyList<Departure> shown = BoardFilter.Apply(
			[Dep("11", 0), Dep("66", 5, cancelled: true), Dep("8", 9)],
			null,
			showCancelled: false);

		Assert.Equal(["11-0", "8-9"], [.. shown.Select(departure => departure.Id)]);
	}

	[Fact]
	public void Several_lines_can_be_picked_at_once()
	{
		IReadOnlyList<Departure> shown = BoardFilter.Apply(
			[Dep("11", 0), Dep("66", 5), Dep("8", 9), Dep("61", 12)],
			["11", "61"],
			showCancelled: false);

		Assert.Equal(["11-0", "61-12"], [.. shown.Select(departure => departure.Id)]);
	}

	[Fact]
	public void Only_this_line_is_exactly_one_line()
	{
		IReadOnlyList<Departure> shown = BoardFilter.Apply(
			[Dep("11", 0), Dep("66", 5), Dep("11", 9)],
			["11"],
			showCancelled: false);

		Assert.Equal(["11-0", "11-9"], [.. shown.Select(departure => departure.Id)]);
	}

	[Fact]
	public void A_picked_line_shows_its_cancelled_rows_only_when_asked_for()
	{
		Departure[] board = [Dep("11", 0, cancelled: true), Dep("11", 5)];

		Assert.Equal(["11-5"], [.. BoardFilter.Apply(board, ["11"], showCancelled: false).Select(departure => departure.Id)]);
		Assert.Equal(2, BoardFilter.Apply(board, ["11"], showCancelled: true).Count);
	}

	[Fact]
	public void Line_names_match_with_or_without_spaces_and_case()
	{
		IReadOnlyList<Departure> shown = BoardFilter.Apply(
			[Dep("S 1", 0), Dep("s1", 5), Dep("11", 9)],
			["S1"],
			showCancelled: false);

		Assert.Equal(["S 1-0", "s1-5"], [.. shown.Select(departure => departure.Id)]);
	}

	[Fact]
	public void Cancelled_rows_of_other_lines_stay_hidden_even_when_cancelled_are_shown()
	{
		IReadOnlyList<Departure> shown = BoardFilter.Apply(
			[Dep("11", 0), Dep("66", 5, cancelled: true), Dep("11", 9, cancelled: true)],
			["11"],
			showCancelled: true);

		Assert.Equal(["11-0", "11-9"], [.. shown.Select(departure => departure.Id)]);
	}

	[Fact]
	public void An_empty_pick_is_no_pick()
	{
		IReadOnlyList<Departure> shown = BoardFilter.Apply(
			[Dep("11", 0)],
			[],
			showCancelled: false);

		Assert.Single(shown);
	}
}
