using System.Text.Json;
using DDjourneys.Core.Storage;

namespace DDjourneys.Tracking.Tests;

/// <summary>Versioned storage: tolerant lists and step-by-step migration.</summary>
public sealed class StorageTests
{
	private sealed class MemoryStore : IKeyValueStore
	{
		public Dictionary<string, string> Values { get; } = [];

		public string? Get(string key) => Values.GetValueOrDefault(key);

		public void Set(string key, string value) => Values[key] = value;

		public void Remove(string key) => Values.Remove(key);
	}

	private static string? Name(JsonElement element) =>
		StoredJson.String(element, "Name") is { Length: > 0 } name ? name : null;

	[Fact]
	public void The_bare_array_of_the_first_releases_is_read()
	{
		StoredRead<string> read = StoredJson.Read("""[{"Name":"Postplatz"},{"Name":"Hauptbahnhof"}]""", Name);

		Assert.True(read.Recognized);
		Assert.Equal(1, read.Version);
		Assert.Equal(["Postplatz", "Hauptbahnhof"], read.Items);
	}

	[Fact]
	public void A_written_list_reads_back_as_a_versioned_envelope()
	{
		string json = StoredJson.Write(new[] { "Postplatz" }, name => new System.Text.Json.Nodes.JsonObject { ["Name"] = name });
		StoredRead<string> read = StoredJson.Read(json, Name);

		Assert.Contains("\"v\":2", json);
		Assert.Equal(StoredJson.CurrentVersion, read.Version);
		Assert.Equal(["Postplatz"], read.Items);
	}

	[Fact]
	public void One_unreadable_entry_costs_only_that_entry()
	{
		StoredRead<string> read = StoredJson.Read("""[{"Name":"A"},{"Name":""},42,{"Name":"B"}]""", Name);

		Assert.True(read.Recognized);
		Assert.Equal(["A", "B"], read.Items);
		Assert.Equal(2, read.Dropped);
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("{\"other\":1}")]
	[InlineData("\"text\"")]
	public void Foreign_text_is_not_recognized(string json)
	{
		Assert.False(StoredJson.Read(json, Name).Recognized);
	}

	[Fact]
	public void A_newer_envelope_is_still_read()
	{
		StoredRead<string> read = StoredJson.Read("""{"v":9,"items":[{"Name":"A","Extra":true}],"more":1}""", Name);

		Assert.Equal(9, read.Version);
		Assert.Equal(["A"], read.Items);
	}

	[Fact]
	public void Fields_are_read_leniently()
	{
		using JsonDocument document = JsonDocument.Parse("""{"id":123,"Lat":"51.05","bad":null}""");
		JsonElement root = document.RootElement;

		Assert.Equal("123", StoredJson.String(root, "Id"));
		Assert.Equal(51.05, StoredJson.Number(root, "lat"));
		Assert.Null(StoredJson.String(root, "bad"));
	}

	[Fact]
	public void Migrations_run_once_in_order_and_record_the_version()
	{
		var store = new MemoryStore();
		var order = new List<string>();

		StorageMigration[] steps =
		[
			new(2, "two", _ => order.Add("two")),
			new(1, "one", _ => order.Add("one"))
		];

		Assert.Equal(["one", "two"], new StorageMigrator(store, steps).Run());
		Assert.Equal("2", store.Values[StorageMigrator.VersionKey]);
		Assert.Empty(new StorageMigrator(store, steps).Run());
		Assert.Equal(["one", "two"], order);
	}

	[Fact]
	public void A_failing_step_is_retried_and_does_not_block_the_version_below_it()
	{
		var store = new MemoryStore();
		bool fail = true;

		StorageMigration[] steps =
		[
			new(1, "one", _ => { }),
			new(2, "two", _ => { if (fail) { throw new InvalidOperationException(); } }),
			new(3, "three", _ => { })
		];

		new StorageMigrator(store, steps).Run();

		Assert.Equal("1", store.Values[StorageMigrator.VersionKey]);

		fail = false;

		Assert.Equal(["two", "three"], new StorageMigrator(store, steps).Run());
		Assert.Equal("3", store.Values[StorageMigrator.VersionKey]);
	}

	[Fact]
	public void Data_of_a_newer_version_is_left_alone()
	{
		var store = new MemoryStore();
		store.Values[StorageMigrator.VersionKey] = "7";
		bool ran = false;

		new StorageMigrator(store, [new StorageMigration(2, "two", _ => ran = true)]).Run();

		Assert.False(ran);
		Assert.Equal("7", store.Values[StorageMigrator.VersionKey]);
	}
}
