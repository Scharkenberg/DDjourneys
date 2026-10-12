using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;
using DDjourneys.Localization;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The last boards kept on the device (B7): the round trip of a board with every field the view shows, the
/// provider/stop/mode key, the LRU bound of thirty with its eviction order, and the wording of the as-of
/// stamp in both languages.
/// </summary>
public sealed class BoardCacheTests
{
	private static readonly DateTimeOffset At = new(2026, 10, 12, 18, 42, 0, TimeSpan.FromHours(2));

	private static Departure Dep(
		string line,
		int minutes,
		string platform = "4",
		DepartureState state = DepartureState.InTime,
		TimeSpan? realtime = null,
		string[]? changes = null) =>
		new()
		{
			Id = $"{line}-{minutes}",
			StopId = "de:14612:12",
			Line = new TransitLine
			{
				Name = line,
				Mode = TransitMode.Tram,
				Destination = "Bühlau"
			},
			Scheduled = At.AddMinutes(minutes),
			Realtime = realtime is { } live ? At.AddMinutes(minutes) + live : null,
			Platform = platform,
			PlatformKind = PlatformKind.Railtrack,
			State = state,
			Occupancy = OccupancyLevel.Low,
			RouteChangeIds = changes ?? []
		};

	private static CachedBoard Board(
		string stop,
		bool arrival = false,
		int minutes = 0,
		params Departure[] departures) =>
		new(
			"vvo",
			$"de:14612:{stop}",
			arrival,
			At,
			departures.Length > 0 ? departures : [Dep("11", minutes)]);

	[Fact]
	public void A_board_round_trips_with_every_field_the_view_shows()
	{
		CachedBoard board =
			Board(
				"Postplatz",
				arrival: true,
				departures:
				[
					Dep("11", 0, platform: "Gl. 2", state: DepartureState.Cancelled, changes: ["rc-1", "rc-2"]),
					Dep("66", 5, realtime: TimeSpan.FromMinutes(3), platform: "3")
				]);

		CachedBoard? read = BoardCacheList.Find(BoardCacheList.Parse(BoardCacheList.Write([board])), "vvo", "de:14612:Postplatz", true);

		Assert.NotNull(read);
		Assert.Equal(At, read!.Fetched);
		Assert.Equal(2, read.Departures.Count);

		Departure first = read.Departures[0];
		Assert.Equal("11-0", first.Id);
		Assert.Equal("de:14612:12", first.StopId);
		Assert.Equal("11", first.Line.Name);
		Assert.Equal(TransitMode.Tram, first.Line.Mode);
		Assert.Equal("Bühlau", first.Line.Destination);
		Assert.Equal(At, first.Scheduled);
		Assert.Null(first.Realtime);
		Assert.Equal("Gl. 2", first.Platform);
		Assert.Equal(PlatformKind.Railtrack, first.PlatformKind);
		Assert.True(first.IsCancelled);
		Assert.Equal(OccupancyLevel.Low, first.Occupancy);
		Assert.Equal(["rc-1", "rc-2"], first.RouteChangeIds);

		Departure second = read.Departures[1];
		Assert.Equal(At.AddMinutes(5), second.Scheduled);
		Assert.Equal(At.AddMinutes(8), second.Realtime);
		Assert.Equal(3, second.Delay!.Value.TotalMinutes);
	}

	[Fact]
	public void A_departure_without_a_time_or_without_an_id_is_not_kept()
	{
		string json =
			"""
			{"v":2,"items":[{"ProviderId":"vvo","StopId":"s1","IsArrival":false,"Fetched":"2026-10-12T18:42:00+02:00","Departures":[
				{"Id":"a","Line":{"Name":"11"},"Scheduled":"2026-10-12T18:45:00+02:00"},
				{"Id":"","Line":{"Name":"66"},"Scheduled":"2026-10-12T18:50:00+02:00"},
				{"Id":"c","Line":{"Name":"8"}}]}
			]}
			""";

		CachedBoard? board = BoardCacheList.Find(BoardCacheList.Parse(json), "vvo", "s1", false);

		Assert.NotNull(board);
		Assert.Single(board!.Departures);
		Assert.Equal("a", board.Departures[0].Id);
		Assert.Equal(TransitMode.Unknown, board.Departures[0].Line.Mode);
		Assert.Equal(PlatformKind.Unknown, board.Departures[0].PlatformKind);
	}

	[Fact]
	public void One_broken_entry_costs_only_that_entry()
	{
		string json =
			"""
			{"v":2,"items":[
				{"ProviderId":"vvo","StopId":"s1","Departures":[]},
				{"ProviderId":"","StopId":"s2","Departures":[]},
				42,
				{"ProviderId":"vvo","StopId":"s3","Departures":[]}
			]}
			""";

		IReadOnlyList<CachedBoard> read = BoardCacheList.Parse(json);

		Assert.Equal(["s1", "s3"], [.. read.Select(board => board.StopId)]);
	}

	[Fact]
	public void Foreign_text_is_no_boards()
	{
		Assert.Empty(BoardCacheList.Parse(null));
		Assert.Empty(BoardCacheList.Parse("not json"));
		Assert.Empty(BoardCacheList.Parse("{\"other\":1}"));
	}

	[Fact]
	public void The_key_is_provider_stop_and_board_mode()
	{
		IReadOnlyList<CachedBoard> list = [];

		foreach (var (provider, stop) in new (string, string)[] { ("vvo", "s1"), ("trias-vvo", "s1"), ("vvo", "s2") })
		{
			list = BoardCacheList.Touch(list, new CachedBoard(provider, stop, IsArrival: false, At, [Dep("11", 0)]));
		}

		list = BoardCacheList.Touch(list, new CachedBoard("vvo", "s1", IsArrival: true, At, [Dep("11", 0)]));

		Assert.Equal(4, list.Count);
		Assert.NotNull(BoardCacheList.Find(list, "vvo", "s1", false));
		Assert.NotNull(BoardCacheList.Find(list, "trias-vvo", "s1", false));
		Assert.Null(BoardCacheList.Find(list, "trias-vvo", "s2", false));
		Assert.NotNull(BoardCacheList.Find(list, "vvo", "s1", true));
		Assert.Null(BoardCacheList.Find(list, "vvo", "s2", true));
	}

	[Fact]
	public void Touch_keeps_at_most_thirty_boards_and_drops_the_one_fetched_longest_ago()
	{
		IReadOnlyList<CachedBoard> list = [];

		for (int stop = 1; stop <= 31; stop++)
		{
			list = BoardCacheList.Touch(list, new CachedBoard("vvo", $"s{stop}", IsArrival: false, At.AddMinutes(stop), [Dep("11", 0)]));
		}

		Assert.Equal(30, list.Count);
		Assert.Null(BoardCacheList.Find(list, "vvo", "s1", false));
		Assert.Equal(["s31", "s30"], [.. list.Take(2).Select(board => board.StopId)]);
		Assert.Equal("s2", list[^1].StopId);
	}

	[Fact]
	public void Fetching_a_stop_again_makes_it_the_newest_without_a_second_entry()
	{
		IReadOnlyList<CachedBoard> list = [];

		foreach (string stop in new[] { "s1", "s2", "s3" })
		{
			list = BoardCacheList.Touch(list, new CachedBoard("vvo", stop, IsArrival: false, At, [Dep("11", 0)]));
		}

		list = BoardCacheList.Touch(list, new CachedBoard("vvo", "s1", IsArrival: false, At.AddMinutes(1), [Dep("66", 2)]));

		Assert.Equal(3, list.Count);
		Assert.Equal("s1", list[0].StopId);
		Assert.Equal(At.AddMinutes(1), list[0].Fetched);
		Assert.Equal(["s3", "s2"], [.. list.Skip(1).Select(board => board.StopId)]);

		// Reading does not change the order.
		Assert.NotNull(BoardCacheList.Find(list, "vvo", "s2", false));
		Assert.Equal("s1", list[0].StopId);
	}

	[Fact]
	public void The_offline_stamp_is_worded_in_both_languages_and_names_the_time()
	{
		string english = new EnglishUiStrings().Departures.CachedBoard;
		string german = new GermanUiStrings().Departures.CachedBoard;

		Assert.StartsWith("The departures could not be loaded", english, StringComparison.Ordinal);
		Assert.StartsWith("Die Abfahrten konnten nicht geladen werden", german, StringComparison.Ordinal);
		Assert.Contains("as of {0}", english, StringComparison.Ordinal);
		Assert.Contains("{0}", german, StringComparison.Ordinal);

		Assert.Contains(
			"18:42",
			string.Format(CultureInfo.CurrentCulture, english, "18:42"),
			StringComparison.Ordinal);
		Assert.Contains(
			"18:42",
			string.Format(CultureInfo.CurrentCulture, german, "18:42"),
			StringComparison.Ordinal);
	}
}
