using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Storage;

namespace DDjourneys.Support;

/// <summary>MAUI Preferences as the key-value store of <see cref="StorageMigrator"/> and <see cref="PlaceStore"/>.</summary>
public sealed class PreferencesKeyValueStore(IPreferences? preferences = null) : IKeyValueStore
{
	private readonly IPreferences _preferences = preferences ?? Preferences.Default;

	public string? Get(string key) =>
		_preferences.ContainsKey(key)
			? _preferences.Get(key, string.Empty)
			: null;

	public void Set(string key, string value) =>
		_preferences.Set(key, value);

	public void Remove(string key) =>
		_preferences.Remove(key);
}

/// <summary>
/// Everything the app keeps on the device is versioned (<c>storage.version</c>) and brought up to date once
/// at the start, before anything reads it, so an update never strands the user's settings, places or tracking
/// state:
/// <list type="bullet">
/// <item>1: appearance settings of the first releases (single "themeId") become mode, colour set and pure black.</item>
/// <item>2: place lists (recents, favourites, searched connections) become the versioned list format; unreadable
/// entries are dropped one by one, an unreadable list is set aside.</item>
/// </list>
/// Journeys and plans of the tracking service live on the service (the account token is kept by
/// <c>SchutzengelTokenStore</c>, with a fallback copy), so there is nothing to convert for them locally.
/// </summary>
public static class AppStorage
{
	public static IReadOnlyList<StorageMigration> Migrations { get; } =
	new List<StorageMigration>
	{
		new StorageMigration(
			1,
			"appearance",
			store =>
			{
				if (store is PreferencesKeyValueStore)
				{
					new AppSettings().MigrateAppearance();
				}
			}),
		new StorageMigration(
			2,
			"place lists",
			store => new PlaceStore(store).Normalize())
	};

	/// <summary>Runs the pending migrations; never throws.</summary>
	public static void Upgrade()
	{
		try
		{
			IReadOnlyList<string> ran =
				new StorageMigrator(new PreferencesKeyValueStore(), Migrations).Run();

			if (ran.Count > 0)
			{
				DiagnosticLog.Write($"Storage upgraded: {string.Join(", ", ran)}");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Storage upgrade skipped: {ex.Message}");
		}
	}
}
