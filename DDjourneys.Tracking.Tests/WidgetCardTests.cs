using System.Text.Json.Nodes;
using DDjourneys.Core.Storage;
using DDjourneys.Core.Widgets;
using DDjourneys.Support.Widgets;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The Windows widget work of Tier 2: the card builder (a snapshot as an Adaptive Card, the host's
/// semantic colours for the statuses, the row cap) and the widget store over an in-memory key-value
/// store - the StorageMigrator seam, as on Windows where the preferences write to the same place.
/// </summary>
public class WidgetCardTests
{
	private static WidgetCardStrings Strings() =>
		new(
			Updated: "Updated {0}",
			Refresh: "Refresh",
			Open: "Open the app",
			SetUp: "Set up this widget",
			CouldNotRefresh: "Could not refresh");

	private static WidgetSnapshot Snapshot(params WidgetRow[] rows) =>
		new()
		{
			Title = "Postplatz",
			UpdatedAt = new DateTimeOffset(2026, 10, 5, 7, 41, 0, TimeSpan.FromHours(2)),
			Rows = rows
		};

	private static WidgetRow Row(
		string chip,
		string time,
		WidgetDelay level = WidgetDelay.None,
		string delay = "",
		string main = "Buehlau") =>
		new()
		{
			Chip = chip,
			Main = main,
			Time = time,
			Delay = delay,
			DelayLevel = level
		};

	private static JsonNode TemplateOf(WidgetCardPayload card) =>
		JsonNode.Parse(card.Template)!;

	[Fact]
	public void A_snapshot_becomes_a_card_with_header_rows_and_two_actions()
	{
		WidgetSnapshot snapshot = Snapshot(Row("8", "07:43", WidgetDelay.Late, "+3"));

		WidgetCardPayload card = WidgetCard.For(snapshot, new WidgetLayout(2, WidgetDetail.Full, true), Strings());
		JsonNode template = TemplateOf(card);

		Assert.Equal("AdaptiveCard", template!["type"]!.GetValue<string>());
		Assert.Equal("Postplatz", template["body"]![0]!["text"]!.GetValue<string>());
		Assert.Contains("07:41", template["body"]![1]!["text"]!.GetValue<string>());

		JsonNode actions = template["actions"]!;
		Assert.Equal(2, actions.AsArray().Count);
		Assert.Equal("refresh", actions[0]!["verb"]!.GetValue<string>());
		Assert.Equal("open", actions[1]!["verb"]!.GetValue<string>());

		Assert.Equal("{}", card.Data);
	}

	[Fact]
	public void The_delay_wears_the_host_s_words_for_it()
	{
		WidgetSnapshot snapshot = Snapshot(
			Row("8", "07:43", WidgetDelay.Late, "+3"),
			Row("66", "07:45", WidgetDelay.Cancelled, "−1"),
			Row("3", "07:46", WidgetDelay.None));

		JsonNode template = TemplateOf(WidgetCard.For(snapshot, new WidgetLayout(3, WidgetDetail.Full, false), Strings()));

		string late = template["body"]![2]!["columns"]![2]!["items"]![1]!["color"]!.GetValue<string>();
		string cancelled = template["body"]![3]!["columns"]![2]!["items"]![1]!["color"]!.GetValue<string>();
		string fine = template["body"]![4]!["columns"]![2]!["items"]![1]!["color"]!.GetValue<string>();

		Assert.Equal("warning", late);
		Assert.Equal("attention", cancelled);
		Assert.Equal("good", fine);
	}

	[Fact]
	public void The_row_cap_leaves_only_as_many_rows_as_fit()
	{
		WidgetSnapshot snapshot = Snapshot(
			Row("8", "07:43"),
			Row("66", "07:45"),
			Row("3", "07:46"));

		JsonNode template = TemplateOf(WidgetCard.For(snapshot, new WidgetLayout(2, WidgetDetail.Full, false), Strings()));

		int columnSets = 0;

		foreach (JsonNode? item in template["body"]!.AsArray())
		{
			if (item!["type"]!.GetValue<string>() == "ColumnSet")
			{
				columnSets++;
			}
		}

		Assert.Equal(2, columnSets);
	}

	[Fact]
	public void A_stale_snapshot_says_so_instead_of_when_it_was_updated()
	{
		WidgetSnapshot snapshot = Snapshot(Row("8", "07:43")) with { IsStale = true };

		JsonNode template = TemplateOf(WidgetCard.For(snapshot, new WidgetLayout(2, WidgetDetail.Full, true), Strings()));

		Assert.Equal("Could not refresh", template["body"]![1]!["text"]!.GetValue<string>());
	}

	[Fact]
	public void A_snapshot_without_rows_shows_its_message()
	{
		WidgetSnapshot snapshot = Snapshot() with { Message = "No departures" };

		JsonNode template = TemplateOf(WidgetCard.For(snapshot, new WidgetLayout(2, WidgetDetail.Full, true), Strings()));

		Assert.Equal("No departures", template["body"]![^1]!["text"]!.GetValue<string>());
	}

	[Fact]
	public void A_widget_without_settings_gets_the_set_up_card()
	{
		WidgetCardPayload card = WidgetCard.SetUp(Strings());
		JsonNode template = TemplateOf(card);

		Assert.Equal("Set up this widget", template["body"]![0]!["text"]!.GetValue<string>());
		Assert.Equal("setup", template["actions"]![0]!["verb"]!.GetValue<string>());
	}

	[Fact]
	public void The_store_round_trips_config_snapshot_and_fetch_time()
	{
		KeyValueWidgetStore store = new(new MemoryStore());

		WidgetConfig config = new() { Kind = WidgetKind.Departures, Stop = new DDjourneys.Core.Models.Location { Id = "33000028", Name = "Postplatz" }, MaxRows = 5 };
		store.SaveConfig("widget-1", config);

		WidgetConfig? read = store.LoadConfig("widget-1");
		Assert.NotNull(read);
		Assert.Equal(WidgetKind.Departures, read!.Kind);
		Assert.Equal(5, read.MaxRows);
		Assert.Null(store.LoadConfig("widget-2"));

		WidgetSnapshot snapshot = Snapshot(Row("8", "07:43")) with { IsStale = true };
		store.SaveSnapshot("widget-1", snapshot);

		Assert.Equal(snapshot.Title, store.LoadSnapshot("widget-1")!.Title);
		Assert.NotNull(store.LastFetched("widget-1"));

		store.ClearSnapshot("widget-1");
		Assert.Null(store.LoadSnapshot("widget-1"));
		Assert.Null(store.LastFetched("widget-1"));
		Assert.NotNull(store.LoadConfig("widget-1"));

		store.Remove("widget-1");
		Assert.Null(store.LoadConfig("widget-1"));
	}

	/// <summary>The in-memory stand-in for the application data settings (the StorageMigrator seam).</summary>
	private sealed class MemoryStore : IKeyValueStore
	{
		private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

		public string? Get(string key) =>
			_values.TryGetValue(key, out string value) ? value : null;

		public void Set(string key, string value) => _values[key] = value;

		public void Remove(string key) => _values.Remove(key);
	}
}
