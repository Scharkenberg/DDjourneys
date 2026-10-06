namespace DDjourneys.Support;

/// <summary>The library that draws the map page: MapLibre GL with CARTO's vector styles, or Leaflet with raster tiles.</summary>
public enum MapEngine
{
	/// <summary>Vector map (WebGL 2, needs the user's CARTO key), recoloured in the app's palette.</summary>
	Carto,

	/// <summary>Raster tiles, no key, no WebGL: works on old devices and web views.</summary>
	Leaflet
}

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

	/// <summary>Which library draws the map (Settings > Map).</summary>
	public static MapEngine Engine
	{
		get
		{
			try
			{
				return string.Equals(Preferences.Default.Get(EnginePreferenceKey, string.Empty), LeafletId, StringComparison.Ordinal)
					? MapEngine.Leaflet
					: MapEngine.Carto;
			}
			catch (Exception)
			{
				return MapEngine.Carto;
			}
		}
	}

	/// <summary>Leaflet needs no key; the CARTO vector map needs a key CARTO accepts.</summary>
	public static bool IsAvailable =>
		Engine == MapEngine.Leaflet
		|| (HasKey && !IsRejected);

	/// <summary>Preferences value of <see cref="MapEngine.Leaflet"/> (the CARTO engine stores nothing but "carto").</summary>
	public const string LeafletId = "leaflet";

	public const string CartoId = "carto";

	/// <summary>The preference the engine is stored in (<see cref="AppSettings.MapEngine"/> writes it).</summary>
	public const string EnginePreferenceKey = "mapEngine";

	/// <summary>Chooses the engine (as text, like every setting) and tells the open maps.</summary>
	public static void SetEngine(MapEngine engine)
	{
		try
		{
			Preferences.Default.Set(EnginePreferenceKey, engine == MapEngine.Leaflet ? LeafletId : CartoId);
		}
		catch (Exception)
		{
			// Not stored: the map stays as it is.
			return;
		}

		Changed?.Invoke(null, EventArgs.Empty);
	}

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
	public static void KeyChanged()
	{
		MapSupport.Retry();
		Changed?.Invoke(null, EventArgs.Empty);
	}
}
