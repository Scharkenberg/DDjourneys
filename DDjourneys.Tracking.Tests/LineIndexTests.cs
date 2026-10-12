using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The line index of the place search's line rows (B1): which queries name a line, the deduplication of
/// one line seen at many stops, per-provider separation, decay and capacity, and the ranking of a search.
/// </summary>
public class LineIndexTests
{
	private static Station Stop(string name) =>
		new()
		{
			Id = name,
			Name = name
		};

	private static TransitLine Line(string name, TransitMode mode = TransitMode.Tram) =>
		new() { Name = name, Mode = mode };

	[Theory]
	[InlineData("11", true)]
	[InlineData("85", true)]
	[InlineData("S1", true)]
	[InlineData("S 1", true)]
	[InlineData("RE01", true)]
	[InlineData("E 85", true)]
	[InlineData("11a", true)]
	[InlineData("2026", true)]
	[InlineData("Postplatz", false)]
	[InlineData("Bahnhof 11", false)]
	[InlineData("1. Mai", false)]
	[InlineData("H3-11", false)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	public void Only_short_digit_forms_name_a_line(string query, bool expected)
	{
		Assert.Equal(expected, LineIndex.IsLineQuery(query));
	}

	[Fact]
	public void The_same_line_at_many_stops_is_one_entry_that_remembers_the_last_stop()
	{
		LineIndex index = new();

		index.Remember("p", Line("11"), Stop("Postplatz"));
		index.Remember("p", Line("11"), Stop("Bühlau"));

		IReadOnlyList<IndexedLine> found = index.Search("p", "11");

		IndexedLine line = Assert.Single(found);
		Assert.Equal("11", line.Name);
		Assert.Equal("Bühlau", line.SeenAt.Name);
	}

	[Fact]
	public void The_name_first_seen_stays_and_an_unknown_mode_may_become_known()
	{
		LineIndex index = new();

		index.Remember("p", Line("S 1", TransitMode.Unknown), Stop("Postplatz"));
		index.Remember("p", Line("S1", TransitMode.SuburbanRail), Stop("Bühlau"));

		IndexedLine line = Assert.Single(index.Search("p", "S1"));

		Assert.Equal("S 1", line.Name);
		Assert.Equal(TransitMode.SuburbanRail, line.Mode);
	}

	[Fact]
	public void Lines_belong_to_their_provider()
	{
		LineIndex index = new();

		index.Remember("p", Line("11"), Stop("Postplatz"));

		Assert.Empty(index.Search("other", "11"));
		Assert.Single(index.Search("p", "11"));
	}

	[Fact]
	public void The_exact_name_comes_first_then_the_shortest_that_begin_with_it()
	{
		LineIndex index = new();

		index.Remember("p", Line("11"), Stop("Postplatz"));
		index.Remember("p", Line("111"), Stop("Postplatz"));
		index.Remember("p", Line("1"), Stop("Postplatz"));
		index.Remember("p", Line("66"), Stop("Postplatz"));

		Assert.Equal(
			["1", "11", "111"],
			[.. index.Search("p", "1").Select(line => line.Name)]);
		Assert.Empty(index.Search("p", "9"));
	}

	[Fact]
	public void A_search_answers_at_most_the_limit()
	{
		LineIndex index = new();

		index.Remember("p", Line("11"), Stop("a"));
		index.Remember("p", Line("111"), Stop("a"));
		index.Remember("p", Line("1111"), Stop("a"));

		Assert.Equal(2, index.Search("p", "1", limit: 2).Count);
	}

	[Fact]
	public void Lines_age_out_when_they_have_not_been_seen()
	{
		LineIndex index = new(lifetime: TimeSpan.FromMilliseconds(1));

		index.Remember("p", Line("11"), Stop("Postplatz"));
		index.Prune(DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5));

		Assert.Empty(index.Search("p", "11"));
	}

	[Fact]
	public void Beyond_the_capacity_the_oldest_go_first()
	{
		LineIndex index = new(capacity: 2);

		index.Remember("p", Line("11"), Stop("a"));
		Thread.Sleep(40);
		index.Remember("p", Line("66"), Stop("a"));
		Thread.Sleep(40);
		index.Remember("p", Line("8"), Stop("a"));

		Assert.Equal(
			["66", "8"],
			[.. index.Search("p", "1").Concat(index.Search("p", "6")).Concat(index.Search("p", "8")).Select(line => line.Name)]);
	}

	[Fact]
	public void Nothing_is_remembered_for_an_empty_provider_or_name()
	{
		LineIndex index = new();

		index.Remember("", Line("11"), Stop("a"));
		index.Remember("p", Line("  "), Stop("a"));

		Assert.Empty(index.Search("p", "11"));
	}

	[Fact]
	public void An_empty_query_answers_nothing()
	{
		LineIndex index = new();

		index.Remember("p", Line("11"), Stop("a"));

		Assert.Empty(index.Search("p", ""));
		Assert.Empty(index.Search("", "11"));
	}
}
