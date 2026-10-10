using DDjourneys.Core.Widgets;
using DDjourneys.Support.Widgets;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>What is kept per widget id: its settings, the last snapshot and when it was fetched.</summary>
/// <remarks>
/// A thin facade for the Android widget code (the updater, the config activity, the providers), so the
/// store itself can live behind <see cref="IWidgetStore"/> like on Windows: every call lands in
/// <see cref="AndroidWidgetStore"/> with the same keys and the same JSON as before the split.
/// </remarks>
internal static class WidgetStore
{
	private static readonly IWidgetStore Store = new AndroidWidgetStore();

	private static string Id(int id) =>
		id.ToString(System.Globalization.CultureInfo.InvariantCulture);

	public static WidgetConfig? LoadConfig(int id) =>
		Store.LoadConfig(Id(id));

	public static void SaveConfig(int id, WidgetConfig config) =>
		Store.SaveConfig(Id(id), config);

	public static WidgetSnapshot? LoadSnapshot(int id) =>
		Store.LoadSnapshot(Id(id));

	public static void SaveSnapshot(int id, WidgetSnapshot snapshot) =>
		Store.SaveSnapshot(Id(id), snapshot);

	/// <summary>When the snapshot was last fetched from the network (also after a failed attempt is not recorded).</summary>
	public static DateTimeOffset? LastFetched(int id) =>
		Store.LastFetched(Id(id));

	/// <summary>Forgets the data (not the settings): after a change of settings the old rows are about something else.</summary>
	public static void ClearSnapshot(int id) =>
		Store.ClearSnapshot(Id(id));

	public static void Remove(int id) =>
		Store.Remove(Id(id));
}
