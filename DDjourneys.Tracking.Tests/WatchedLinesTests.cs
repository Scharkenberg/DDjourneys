using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Core.Storage;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The watched lines of B4: the stored lists (round-trip, provider scoping), the disruption state of a line
/// (fingerprint, change detection), the watch set of a check (stored + ephemeral union) and the skipped-stop
/// logic of the line-course diversions.
/// </summary>
public sealed class WatchedLineListTests
{
	[Fact]
	public void Watched_lines_survive_a_round_trip()
	{
		var lines =
			new List<WatchedLine>
			{
					new("vvo", "S 1"),
					new("trias-vvo", "11")
			};

		IReadOnlyList<WatchedLine> read = WatchedLineList.Parse(WatchedLineList.Write(lines));

		Assert.Equal(2, read.Count);
		Assert.Equal("vvo", read[0].ProviderId);
		Assert.Equal("S 1", read[0].Name);
		Assert.Equal("trias-vvo", read[1].ProviderId);
		Assert.Equal("11", read[1].Name);
	}

	[Fact]
	public void Entries_that_do_not_fit_cost_themselves_not_the_list()
	{
		string json =
			"""{"v":2,"items":[{"ProviderId":"vvo","Name":"11"},{"nothing":"here"},{"Name":"12"}]}""";

		IReadOnlyList<WatchedLine> read = WatchedLineList.Parse(json);

		WatchedLine only = Assert.Single(read);
		Assert.Equal("vvo", only.ProviderId);
		Assert.Equal("11", only.Name);
	}

	[Fact]
	public void The_same_line_rule_tells_membership_and_toggling()
	{
		List<WatchedLine> lines = [new("vvo", "S 1")];

		Assert.True(WatchedLineList.Contains(lines, "vvo", "s1"));
		Assert.True(WatchedLineList.Contains(lines, "VVO", "S  1"));
		Assert.False(WatchedLineList.Contains(lines, "vvo", "S 2"));
		Assert.False(WatchedLineList.Contains(lines, "trias-vvo", "S 1"));

		IReadOnlyList<WatchedLine> watched = WatchedLineList.With(lines, "vvo", "61", true);
		Assert.Equal(2, watched.Count);

		// Watching the same line again under another spelling replaces, never doubles.
		IReadOnlyList<WatchedLine> again = WatchedLineList.With(watched, "vvo", "s 1", true);
		Assert.Equal(2, again.Count);

		IReadOnlyList<WatchedLine> forgotten = WatchedLineList.With(again, "vvo", "S1", false);
		WatchedLine only = Assert.Single(forgotten);
		Assert.Equal("61", only.Name);
	}

	[Fact]
	public void Provider_scoping_keeps_other_providers_lines()
	{
		List<WatchedLine> lines = [new("vvo", "11"), new("trias-vvo", "11")];

		IReadOnlyList<WatchedLine> kept = WatchedLineList.With(lines, "vvo", "11", false);

		WatchedLine only = Assert.Single(kept);
		Assert.Equal("trias-vvo", only.ProviderId);
	}

	[Fact]
	public void States_survive_a_round_trip_and_tolerate_a_missing_fingerprint()
	{
		string written =
			WatchedLineStateList.Write(
			[
					new WatchedLineState("vvo", "S1", "ABC"),
					new WatchedLineState("vvo", "11", "")
			]);

		IReadOnlyList<WatchedLineState> read = WatchedLineStateList.Parse(written);

		Assert.Equal(2, read.Count);
		Assert.Equal("ABC", read[0].Fingerprint);
		Assert.Equal(string.Empty, read[1].Fingerprint);

		// A stored entry without a fingerprint (an older write) still reads.
		string json =
			"""{"v":2,"items":[{"ProviderId":"vvo","Line":"11"},{"ProviderId":"vvo","Line":"61","Fingerprint":"X"}]}""";

		IReadOnlyList<WatchedLineState> partial = WatchedLineStateList.Parse(json);

		Assert.Equal(2, partial.Count);
		Assert.Equal(string.Empty, partial[0].Fingerprint);
		Assert.Equal("X", partial[1].Fingerprint);
	}
}

public sealed class LineDisruptionsTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(2));

	private static Disruption Change(
		string id,
		string line,
		string? title = null,
		DateTimeOffset? published = null,
		DateTimeOffset? begin = null,
		DateTimeOffset? end = null) =>
		new()
		{
			Id = id,
			Title = title ?? id,
			Lines =
			[
					new DisruptionLine { Id = "line-" + line, Name = line }
			],
			Published = published,
			Periods = begin is null && end is null ? [] : [new DisruptionPeriod { Begin = begin, End = end }]
		};

	[Fact]
	public void A_change_names_a_line_under_the_same_line_rule_and_only_while_active()
	{
		Disruption active = Change("a", "S 1", begin: Now.AddHours(-1), end: Now.AddHours(1));
		Disruption over = Change("b", "S 1", begin: Now.AddHours(-3), end: Now.AddHours(-2));
		Disruption other = Change("c", "S 2", begin: Now.AddHours(-1), end: Now.AddHours(1));

		// AffectsLine matches names only; whether a change is current is the caller's filter (see Report).
		Assert.True(LineDisruptions.AffectsLine(active, "s1"));
		Assert.True(LineDisruptions.AffectsLine(over, "s1"));
		Assert.False(LineDisruptions.AffectsLine(other, "s1"));

		IReadOnlyList<LineDisruptionReport> reports =
			LineDisruptions.Report(["S 1"], [active, over, other], Now);

		LineDisruptionReport report = Assert.Single(reports);
		Assert.True(report.HasActive);
		Assert.Same(active, Assert.Single(report.Active));
	}

	[Fact]
	public void The_fingerprint_is_stable_regardless_of_order_and_follows_every_change()
	{
		Disruption first = Change("a", "11", published: Now.AddDays(-2));
		Disruption second = Change("b", "11", published: Now.AddDays(-1));

		string one = LineDisruptions.Fingerprint([first, second]);
		string two = LineDisruptions.Fingerprint([second, first]);

		Assert.Equal(one, two);

		// Added, removed, re-published: every one of them is a change.
		Assert.NotEqual(one, LineDisruptions.Fingerprint([first]));
		Assert.NotEqual(one, LineDisruptions.Fingerprint([first, second, Change("c", "11")]));
		Assert.NotEqual(
			one,
			LineDisruptions.Fingerprint(
			[
					first,
					Change("b", "11", published: Now)
			]));

		// The same set read again says nothing.
		Assert.Equal(one, LineDisruptions.Fingerprint([Change("a", "11", published: Now.AddDays(-2)), second]));
	}

	[Fact]
	public void The_first_sight_of_a_line_is_a_baseline_and_every_later_move_is_a_change()
	{
		IReadOnlyList<LineDisruptionReport> before =
			LineDisruptions.Report(["11"], [Change("a", "11", published: Now)], Now);

		// No stored state: the baseline, not news.
		LineDisruptionChange baseline = Assert.Single(LineDisruptions.Diff([], "vvo", before));
		Assert.Equal(LineDisruptionChangeKind.FirstSeen, baseline.Kind);

		// The same state again: nothing.
		List<WatchedLineState> state =
		[
			new("vvo", "11", before[0].Fingerprint)
		];
		Assert.Empty(LineDisruptions.Diff(state, "vvo", before));

		// The disruptions moved on: a change.
		IReadOnlyList<LineDisruptionReport> after =
			LineDisruptions.Report(
			["11"],
			[Change("a", "11", published: Now), Change("b", "11", published: Now)],
			Now);

		LineDisruptionChange changed = Assert.Single(LineDisruptions.Diff(state, "vvo", after));
		Assert.Equal(LineDisruptionChangeKind.Changed, changed.Kind);
		Assert.Equal(2, changed.Active.Count);

		// The state of a line that is no longer watched is not news: the only entry names the new line.
		IReadOnlyList<LineDisruptionChange> moved =
				LineDisruptions.Diff(state, "vvo", LineDisruptions.Report(["61"], [], Now));

		LineDisruptionChange first = Assert.Single(moved);
		Assert.Equal("61", first.LineName);
		Assert.Equal(LineDisruptionChangeKind.FirstSeen, first.Kind);

		// Another provider's state never matches.
		Assert.Single(LineDisruptions.Diff(state, "trias-vvo", before));
	}

	[Fact]
	public void The_watch_set_unions_stored_and_ephemeral_lines_without_doubles()
	{
		List<WatchedLine> stored =
		[
				new("vvo", "S 1"),
				new("trias-vvo", "11")
		];

		IReadOnlyList<string> watchSet =
			LineDisruptions.WatchSet(stored, "vvo", ["s1", "61", "", "85"]);

		Assert.Equal(["61", "85", "S1"], watchSet);
		Assert.Empty(LineDisruptions.WatchSet([], "vvo", []));
	}

	[Fact]
	public void The_ride_lines_of_a_journey_are_its_lines_without_walks()
	{
		Journey journey =
			new()
			{
					From = new Station { Id = "a", Name = "A" },
					To = new Station { Id = "e", Name = "E" },
					Legs =
					[
							new JourneyLeg
							{
									Mode = TransitMode.Walk,
									From = new Station { Id = "a", Name = "A" },
									To = new Station { Id = "b", Name = "B" }
							},
							new JourneyLeg
							{
									Mode = TransitMode.Tram,
									Line = new TransitLine { Name = "11", Mode = TransitMode.Tram },
									From = new Station { Id = "b", Name = "B" },
									To = new Station { Id = "c", Name = "C" }
							},
							new JourneyLeg
							{
									Mode = TransitMode.Bus,
									Line = new TransitLine { Name = "11", Mode = TransitMode.Bus },
									From = new Station { Id = "c", Name = "C" },
									To = new Station { Id = "d", Name = "D" }
							},
							new JourneyLeg
							{
									Mode = TransitMode.SuburbanRail,
									Line = new TransitLine { Name = "S 1", Mode = TransitMode.SuburbanRail },
									From = new Station { Id = "d", Name = "D" },
									To = new Station { Id = "e", Name = "E" }
							}
					]
			};

		Assert.Equal(["11", "S 1"], LineDisruptions.RideLines(journey));
	}
}

public sealed class LineDiversionsTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(2));

	private static RunStop Stop(string id, string name, bool cancelled = false) =>
		new()
		{
			Station = new Station { Id = id, Name = name, Latitude = 51.05, Longitude = 13.73 },
			Scheduled = Now,
			State = cancelled ? DepartureState.Cancelled : DepartureState.InTime
		};

	[Fact]
	public void Skipped_stops_are_exactly_the_cancelled_ones_of_the_course()
	{
		LineCourse course =
			new(
				"11",
				TransitMode.Tram,
				null,
				[Stop("a", "A"), Stop("b", "B", true), Stop("c", "C", true), Stop("d", "D")],
				[],
				"A",
				"D");

		IReadOnlyList<RunStop> skipped = LineDiversions.SkippedStops(course);

		Assert.Equal(["B", "C"], skipped.Select(stop => stop.Station.Name));
	}

	[Fact]
	public void Active_changes_of_a_line_follow_validity_and_the_same_line_rule()
	{
		Disruption active = new() { Id = "a", Title = "Umleitung", Lines = [new DisruptionLine { Id = "l", Name = "11" }] };
		Disruption inactive =
			new()
			{
					Id = "b",
					Title = "Alt",
					Lines = [new DisruptionLine { Id = "l2", Name = "11" }],
					Periods = [new DisruptionPeriod { Begin = Now.AddDays(-5), End = Now.AddDays(-1) }]
			};
		Disruption other = new() { Id = "c", Title = "Woanders", Lines = [new DisruptionLine { Id = "l3", Name = "61" }] };

		IReadOnlyList<Disruption> changes =
			LineDiversions.ActiveChangesOf([active, inactive, other], "11", Now);

		Assert.Same(active, Assert.Single(changes));
		Assert.Empty(LineDiversions.ActiveChangesOf([active], "61", Now));
	}

	[Fact]
	public void The_notice_text_leads_with_the_title_and_keeps_the_description()
	{
		Disruption change =
			new() { Id = "a", Title = "Umleitung Linie 11", Description = "Die Haltestellen fallen aus." };

		Assert.Equal("Umleitung Linie 11\n\nDie Haltestellen fallen aus.", LineDiversions.NoticeText(change));

		Disruption bare = new() { Id = "b", Title = "Nur der Titel" };

		Assert.Equal("Nur der Titel", LineDiversions.NoticeText(bare));
	}
}
