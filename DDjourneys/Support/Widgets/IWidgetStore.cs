using DDjourneys.Core.Widgets;

namespace DDjourneys.Support.Widgets;

/// <summary>
/// What is kept per widget id: its settings, the last snapshot and when it was fetched. One interface for
/// both platforms (Android: the preferences; Windows: the application data settings); the JSON formats are
/// the app's own (WidgetConfig, WidgetSnapshot), so <c>widget-data</c> (r2: journey rows carry arrival, duration, transfers, lines and lead) is one revision for both.
/// </summary>
public interface IWidgetStore
{
	WidgetConfig? LoadConfig(string id);

	void SaveConfig(string id, WidgetConfig config);

	WidgetSnapshot? LoadSnapshot(string id);

	/// <summary>Keeps the snapshot and notes when it was fetched (a failed attempt is not noted).</summary>
	void SaveSnapshot(string id, WidgetSnapshot snapshot);

	/// <summary>When the snapshot was last fetched from the network.</summary>
	DateTimeOffset? LastFetched(string id);

	/// <summary>Forgets the data (not the settings): after a change of settings the old rows are about something else.</summary>
	void ClearSnapshot(string id);

	void Remove(string id);
}
