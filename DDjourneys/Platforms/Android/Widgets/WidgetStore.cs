using DDjourneys.Core.Widgets;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>What is kept per widget id: its settings, the last snapshot and when it was fetched.</summary>
internal static class WidgetStore
{
	private static string Key(int id, string part) =>
		$"widget.{id}.{part}";

	public static WidgetConfig? LoadConfig(int id) =>
		WidgetConfig.FromJson(Preferences.Default.Get(Key(id, "config"), string.Empty));

	public static void SaveConfig(int id, WidgetConfig config) =>
		Preferences.Default.Set(Key(id, "config"), config.ToJson());

	public static WidgetSnapshot? LoadSnapshot(int id) =>
		WidgetSnapshot.FromJson(Preferences.Default.Get(Key(id, "snapshot"), string.Empty));

	public static void SaveSnapshot(int id, WidgetSnapshot snapshot)
	{
		Preferences.Default.Set(Key(id, "snapshot"), snapshot.ToJson());
		Preferences.Default.Set(Key(id, "fetched"), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
	}

	/// <summary>When the snapshot was last fetched from the network (also after a failed attempt is not recorded).</summary>
	public static DateTimeOffset? LastFetched(int id) =>
		Preferences.Default.Get(Key(id, "fetched"), 0L) is > 0 and var seconds
			? DateTimeOffset.FromUnixTimeSeconds(seconds)
			: null;

	/// <summary>Forgets the data (not the settings): after a change of settings the old rows are about something else.</summary>
	public static void ClearSnapshot(int id)
	{
		Preferences.Default.Remove(Key(id, "snapshot"));
		Preferences.Default.Remove(Key(id, "fetched"));
	}

	public static void Remove(int id)
	{
		foreach (string part in new[] { "config", "snapshot", "fetched" })
		{
			Preferences.Default.Remove(Key(id, part));
		}
	}
}
