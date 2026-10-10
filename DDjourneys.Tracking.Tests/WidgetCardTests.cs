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
			CouldNotRefresh: "Could not refresh",
			SetUpHint: "Choose a stop or a route in the app.");

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

	private static JsonArray Body(JsonNode template) => template["body"]!.AsArray();

	private static IEnumerable<JsonNode> RowsOf(JsonNode template) =>
		Body(template).Where(item => item!["type"]!.GetValue<string>() == "ColumnSet").Select(item => item!);

	[Fact]
	public void A_snapshot_becomes_a_card_with_the_stop_as_header_rows_a_stamp_and_actions()
	{
		WidgetSnapshot snapshot = Snapshot(Row("8", "07:43", WidgetDelay.Late, "+3"));

		WidgetCardPayload card = WidgetCard.For(snapshot, WidgetCardSize.Medium, 0, Strings(), now: snapshot.UpdatedAt);
		JsonNode template = TemplateOf(card);

		Assert.Equal("AdaptiveCard", template["type"]!.GetValue<string>());
		Assert.Equal("1.6", template["version"]!.GetValue<string>());
		Assert.Equal("Postplatz", template["header"]!.GetValue<string>());
		Assert.Single(RowsOf(template));
		Assert.Contains(snapshot.UpdatedAt!.Value.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.CurrentCulture), Body(template)[^1]!["text"]!.GetValue<string>());

		JsonNode actions = template["actions"]!;
		Assert.Equal(2, actions.AsArray().Count);
		Assert.Equal("refresh", actions[0]!["verb"]!.GetValue<string>());
		Assert.Equal("open", actions[1]!["verb"]!.GetValue<string>());
		Assert.Equal("open", template["selectAction"]!["verb"]!.GetValue<string>());

		Assert.Equal("{}", card.Data);
	}

	[Fact]
	public void An_empty_title_override_does_not_hide_the_stop()
	{
		JsonNode template = TemplateOf(WidgetCard.For(Snapshot(Row("8", "07:43")), WidgetCardSize.Medium, 0, Strings(), titleOverride: string.Empty));

		Assert.Equal("Postplatz", template["header"]!.GetValue<string>());
	}

	[Fact]
	public void The_delay_wears_the_host_s_words_for_it()
	{
		WidgetSnapshot snapshot = Snapshot(
			Row("8", "07:43", WidgetDelay.Late, "+3"),
			Row("66", "07:45", WidgetDelay.Cancelled, "-1"),
			Row("3", "07:46", WidgetDelay.None, "0"));

		JsonNode[] rows = [.. RowsOf(TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Large, 0, Strings(), now: snapshot.UpdatedAt)))];

		// The time column is the last one; its second item is the delay.
		string Colour(JsonNode row) => row["columns"]![2]!["items"]![1]!["color"]!.GetValue<string>();

		Assert.Equal("warning", Colour(rows[0]));
		Assert.Equal("attention", Colour(rows[1]));
		Assert.Equal("good", Colour(rows[2]));
	}

	[Fact]
	public void The_size_and_the_users_cap_decide_how_many_rows_are_shown()
	{
		WidgetSnapshot snapshot = Snapshot(
			Row("8", "07:43"),
			Row("66", "07:45"),
			Row("3", "07:46"),
			Row("1", "07:47"),
			Row("2", "07:48"));

		Assert.Equal(2, RowsOf(TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Small, 0, Strings(), now: snapshot.UpdatedAt))).Count());
		Assert.Equal(4, RowsOf(TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Medium, 0, Strings(), now: snapshot.UpdatedAt))).Count());
		Assert.Equal(5, RowsOf(TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Large, 0, Strings(), now: snapshot.UpdatedAt))).Count());
		Assert.Equal(3, RowsOf(TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Large, 3, Strings(), now: snapshot.UpdatedAt))).Count());
		Assert.Equal(1, WidgetCard.RowsFor(WidgetCardSize.Medium, 1));
	}

	[Fact]
	public void A_small_widget_has_no_actions_and_no_second_line()
	{
		WidgetSnapshot snapshot = Snapshot(Row("8", "07:43", WidgetDelay.Late, "+3") with { Sub = "Platform 2" });

		JsonNode template = TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Small, 0, Strings(), now: snapshot.UpdatedAt));
		JsonNode row = RowsOf(template).Single();

		Assert.Empty(template["actions"]!.AsArray());
		Assert.Single(row["columns"]![1]!["items"]!.AsArray());

		// The delay colours the time itself.
		Assert.Equal("warning", row["columns"]![2]!["items"]![0]!["color"]!.GetValue<string>());
	}

	[Fact]
	public void A_journey_row_says_when_to_leave_and_when_to_arrive()
	{
		WidgetRow journey =
			Row("7", "23:09") with
			{
				Arrival = "23:17",
				Duration = "8 min",
				Transfers = "Direct",
				Lines = "7"
			};

		JsonNode template = TemplateOf(WidgetCard.For(Snapshot(journey), WidgetCardSize.Medium, 0, Strings(), now: Snapshot(journey).UpdatedAt));
		JsonNode main = RowsOf(template).Single()["columns"]![1]!["items"]!;

		Assert.Equal("23:09 \u2192 23:17", main[0]!["text"]!.GetValue<string>());
		Assert.Equal("8 min \u00b7 Direct \u00b7 7", main[1]!["text"]!.GetValue<string>());
	}

	[Fact]
	public void A_drawn_chip_replaces_the_text_chip()
	{
		WidgetSnapshot snapshot = Snapshot(Row("7", "23:09"));

		JsonNode withImage =
			RowsOf(
				TemplateOf(
					WidgetCard.For(
						snapshot,
						WidgetCardSize.Medium,
						0,
						Strings(),
						chips: _ => new WidgetChipImage("data:image/png;base64,AAAA", 40, 24),
						now: snapshot.UpdatedAt))).Single()["columns"]![0]!["items"]![0]!;

		JsonNode withoutImage =
			RowsOf(TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Medium, 0, Strings(), now: snapshot.UpdatedAt))).Single()["columns"]![0]!["items"]![0]!;

		Assert.Equal("Image", withImage["type"]!.GetValue<string>());
		Assert.Equal("40px", withImage["width"]!.GetValue<string>());
		Assert.Equal("Container", withoutImage["type"]!.GetValue<string>());
	}

	[Fact]
	public void A_stale_snapshot_says_so_instead_of_when_it_was_updated()
	{
		WidgetSnapshot snapshot = Snapshot(Row("8", "07:43")) with { IsStale = true };

		JsonNode template = TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Medium, 0, Strings(), now: snapshot.UpdatedAt));

		Assert.Equal("Could not refresh", Body(template)[^1]!["text"]!.GetValue<string>());
	}

	[Fact]
	public void A_snapshot_without_rows_shows_its_message()
	{
		WidgetSnapshot snapshot = Snapshot() with { Message = "No departures" };

		JsonNode template = TemplateOf(WidgetCard.For(snapshot, WidgetCardSize.Medium, 0, Strings()));

		Assert.Equal("No departures", Body(template)[0]!["text"]!.GetValue<string>());
	}

	[Fact]
	public void A_widget_without_settings_gets_the_set_up_card()
	{
		WidgetCardPayload card = WidgetCard.SetUp(Strings());
		JsonNode template = TemplateOf(card);

		Assert.Equal("Set up this widget", template["body"]![0]!["text"]!.GetValue<string>());
		Assert.Equal("Choose a stop or a route in the app.", template["body"]![1]!["text"]!.GetValue<string>());
		Assert.Equal("setup", template["actions"]![0]!["verb"]!.GetValue<string>());
		Assert.Null(template["header"]);
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
			_values.TryGetValue(key, out string? value) ? value : null;

		public void Set(string key, string value) => _values[key] = value;

		public void Remove(string key) => _values.Remove(key);
	}
}
