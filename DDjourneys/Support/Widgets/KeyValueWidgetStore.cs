using DDjourneys.Core.Widgets;
using DDjourneys.Core.Storage;

namespace DDjourneys.Support.Widgets;

/// <summary>
/// A widget store over <see cref="IKeyValueStore"/>: on Windows the application data settings (the app's
/// Preferences on that platform write there), in the tests a dictionary. The same key scheme and the same
/// JSON as the Android store, so a widget id and its settings mean the same thing wherever they were
/// written; the Windows widget ids are the host's (GUID strings), the keys stay <c>widget.&lt;id&gt;.&lt;part&gt;</c>.
/// </summary>
public sealed class KeyValueWidgetStore(IKeyValueStore store) : IWidgetStore
{
	private static string Key(string id, string part) =>
		$"widget.{id}.{part}";

	public WidgetConfig? LoadConfig(string id) =>
		WidgetConfig.FromJson(store.Get(Key(id, "config")));

	public void SaveConfig(string id, WidgetConfig config) =>
		store.Set(Key(id, "config"), config.ToJson());

	public WidgetSnapshot? LoadSnapshot(string id) =>
		WidgetSnapshot.FromJson(store.Get(Key(id, "snapshot")));

	public void SaveSnapshot(string id, WidgetSnapshot snapshot)
	{
		store.Set(Key(id, "snapshot"), snapshot.ToJson());
		store.Set(Key(id, "fetched"), DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
	}

	public DateTimeOffset? LastFetched(string id) =>
		store.Get(Key(id, "fetched")) is { Length: > 0 } text
			&& long.TryParse(text, out long seconds)
				? DateTimeOffset.FromUnixTimeSeconds(seconds)
				: null;

	public void ClearSnapshot(string id)
	{
		store.Remove(Key(id, "snapshot"));
		store.Remove(Key(id, "fetched"));
	}

	public void Remove(string id)
	{
		foreach (string part in new[] { "config", "snapshot", "fetched" })
		{
			store.Remove(Key(id, part));
		}
	}
}
