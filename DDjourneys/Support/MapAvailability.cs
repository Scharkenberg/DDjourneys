namespace DDjourneys.Support;

/// <summary>
/// Whether the map can be shown. The basemap is CARTO's and needs the user's own API key (Settings > Map): without one,
/// or when CARTO does not accept it, the map is switched off and says why instead of showing a blank view.
/// </summary>
public static class MapAvailability
{
	/// <summary>The preference the key is stored in (<see cref="AppSettings.MapApiKey"/> writes it).</summary>
	public const string PreferenceKey = "cartoApiKey";

	private static string? _rejectedKey;

	/// <summary>Raised (on any thread) when the key or its acceptance changed.</summary>
	public static event EventHandler? Changed;

	/// <summary>The key, trimmed; empty when none was entered.</summary>
	public static string Key
	{
		get
		{
			try
			{
				return Preferences.Default.Get(PreferenceKey, string.Empty).Trim();
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}
	}

	public static bool HasKey => Key.Length > 0;

	/// <summary>CARTO answered the current key with "unauthorised"/"forbidden".</summary>
	public static bool IsRejected =>
		_rejectedKey is { } rejected
		&& string.Equals(rejected, Key, StringComparison.Ordinal);

	public static bool IsAvailable => HasKey && !IsRejected;

	/// <summary>The map page reports that CARTO refused the key.</summary>
	public static void Reject()
	{
		string key = Key;

		if (key.Length == 0
			|| string.Equals(_rejectedKey, key, StringComparison.Ordinal))
		{
			return;
		}

		_rejectedKey = key;
		Changed?.Invoke(null, EventArgs.Empty);
	}

	/// <summary>The key was changed (a new key gets its own chance).</summary>
	public static void KeyChanged() =>
		Changed?.Invoke(null, EventArgs.Empty);
}
