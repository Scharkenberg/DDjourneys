using DDjourneys.Core.Widgets;
using DDjourneys.Support.Widgets;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>
/// The Android widget store, as it always was (the preferences, keys widget.&lt;id&gt;.&lt;part&gt;), now behind
/// <see cref="IWidgetStore"/>: the same bytes in the same places, the interface for the platform-neutral parts.
/// </summary>
internal sealed class AndroidWidgetStore : IWidgetStore
{
	private static string Key(string id, string part) =>
		$"widget.{id}.{part}";

	public WidgetConfig? LoadConfig(string id) =>
		WidgetConfig.FromJson(Preferences.Default.Get(Key(id, "config"), string.Empty));

	public void SaveConfig(string id, WidgetConfig config) =>
		Preferences.Default.Set(Key(id, "config"), config.ToJson());

	public WidgetSnapshot? LoadSnapshot(string id) =>
		WidgetSnapshot.FromJson(Preferences.Default.Get(Key(id, "snapshot"), string.Empty));

	public void SaveSnapshot(string id, WidgetSnapshot snapshot)
	{
		Preferences.Default.Set(Key(id, "snapshot"), snapshot.ToJson());
		Preferences.Default.Set(Key(id, "fetched"), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
	}

	/// <summary>When the snapshot was last fetched from the network (a failed attempt is not recorded).</summary>
	public DateTimeOffset? LastFetched(string id) =>
		Preferences.Default.Get(Key(id, "fetched"), 0L) is > 0 and var seconds
				? DateTimeOffset.FromUnixTimeSeconds(seconds)
				: null;

	/// <summary>Forgets the data (not the settings): after a change of settings the old rows are about something else.</summary>
	public void ClearSnapshot(string id)
	{
		Preferences.Default.Remove(Key(id, "snapshot"));
		Preferences.Default.Remove(Key(id, "fetched"));
	}

	public void Remove(string id)
	{
		foreach (string part in new[] { "config", "snapshot", "fetched" })
		{
			Preferences.Default.Remove(Key(id, part));
		}
	}
}
