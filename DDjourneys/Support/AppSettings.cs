using DDjourneys.Core.Diagnostics;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Storage;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support;

/// <summary>
/// Typed, crash-proof wrapper around <see cref="IPreferences"/>.
/// Every read falls back to its default and every write is swallowed on failure
/// (preferences can throw on corrupted stores or locked-down platforms).
/// Register as a singleton; <see cref="Changed"/> fires with the property name.
/// </summary>
public sealed class AppSettings
{
	public const int MinResults = 3;
	public const int MaxResultsLimit = 10;

	private readonly IPreferences _prefs;

	public AppSettings() : this(Preferences.Default)
	{
	}

	public AppSettings(IPreferences prefs)
	{
		ArgumentNullException.ThrowIfNull(prefs);
		_prefs = prefs;
	}

	public event EventHandler<string>? Changed;

	// ----- App -----

	/// <summary>Light/dark mode: "system", "light" or "dark".</summary>
	public string ThemeMode
	{
		get => Read("themeMode", "system");
		set => Write("themeMode", value);
	}

	/// <summary>Id of the colour set from <see cref="ColorCatalog"/>.</summary>
	public string ThemeColor
	{
		get => Read("themeColor", ColorCatalog.DefaultId);
		set => Write("themeColor", value);
	}

	/// <summary>Dark mode only: pure black surfaces.</summary>
	public bool ThemePureBlack
	{
		get => Read("themePureBlack", true);
		set => Write("themePureBlack", value);
	}

	/// <summary>UI density id: "compact", "normal" or "touch" (see DensityProfile).</summary>
	public string UiDensity
	{
		get => Read("uiDensity", "normal");
		set => Write("uiDensity", value);
	}

	/// <summary>Windows window material: "none", "mica", "micaalt" or "acrylic" (see MaterialProfile; ignored elsewhere).</summary>
	public string WindowMaterial
	{
		get => Read("windowMaterial", "mica");
		set => Write("windowMaterial", value);
	}

	/// <summary>How far the window material reaches: "backdrop", "layered" or "immersive".</summary>
	public string MaterialSurfaces
	{
		get => Read("materialSurfaces", "layered");
		set => Write("materialSurfaces", value);
	}

	/// <summary>Font face id from <see cref="FontCatalog"/>.</summary>
	public string FontFace
	{
		get => Read("fontFace", FontCatalog.OpenSansId);
		set => Write("fontFace", value);
	}

	/// <summary>
	/// One-time migration of the former single "themeId" (or the even older "theme") into mode, colour set and pure black.
	/// Does nothing once "themeMode" exists.
	/// </summary>
	public void MigrateAppearance()
	{
		try
		{
			if (_prefs.ContainsKey("themeMode"))
			{
				return;
			}

			string legacy = Read("themeId", string.Empty);

			if (legacy.Length == 0)
			{
				legacy = Read("theme", "System");
			}

			(string mode, string color, bool black) = ColorCatalog.FromLegacy(legacy);

			_prefs.Set("themeMode", mode);
			_prefs.Set("themeColor", color);
			_prefs.Set("themePureBlack", black);
			_prefs.Remove("themeId");
			_prefs.Remove("theme");
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Appearance migration skipped: {ex.Message}");
		}
	}

	/// <summary>UI language as an IETF culture code (for example "en" or "de-DE").</summary>
	public string LanguageCode
	{
		get => Read("language", CultureInfo.CurrentUICulture.Name);
		set => Write("language", NormalizeCulture(value));
	}

	/// <summary>Subtle transitions and entrance animations. Off = instant UI.</summary>
	public bool Animations
	{
		get => Read("animations", true);
		set => Write("animations", value);
	}

	/// <summary>Shows the seconds-precise time line and technical notice details.</summary>
	public bool ShowTechnicalDetails
	{
		get => Read("technical", false);
		set => Write("technical", value);
	}

	// ----- Journey / provider -----

	/// <summary>Id of the selected data provider (empty = the first registered one).</summary>
	public string ProviderId
	{
		get => Read("providerId", string.Empty);
		set => Write("providerId", value);
	}

	/// <summary>How many journeys to request (clamped to 3..10).</summary>
	public int MaxResults
	{
		get => Math.Clamp(Read("maxResults", 5), MinResults, MaxResultsLimit);
		set => Write("maxResults", Math.Clamp(value, MinResults, MaxResultsLimit));
	}

	/// <summary>Start the Plan page in "Arrive by" mode.</summary>
	public bool DefaultArrival
	{
		get => Read("defaultArrival", false);
		set => Write("defaultArrival", value);
	}

	/// <summary>Show walking legs and platform-internal transfers in the timeline.</summary>
	public bool ShowWalkingLegs
	{
		get => Read("walkingLegs", true);
		set => Write("walkingLegs", value);
	}

	/// <summary>Expand the notices of a journey by default.</summary>
	public bool ExpandNotices
	{
		get => Read("expandNotices", false);
		set => Write("expandNotices", value);
	}

	/// <summary>Minutes before departure at which a newly followed journey starts to be monitored and alerted (1..60).</summary>
	public int DefaultLeadMinutes
	{
		get => Math.Clamp(Read("leadMinutes", 5), 1, 60);
		set => Write("leadMinutes", Math.Clamp(value, 1, 60));
	}

	/// <summary>Request timeout for provider calls, seconds (5..60).</summary>
	public int TimeoutSeconds
	{
		get => Math.Clamp(Read("timeout", 15), 5, 60);
		set => Write("timeout", Math.Clamp(value, 5, 60));
	}

	// ----- Places -----

	/// <summary>The place search also finds addresses (the router adds the walk to the stop).</summary>
	public bool SearchAddresses
	{
		get => Read("searchAddresses", true);
		set => Write("searchAddresses", value);
	}

	/// <summary>The place search also finds points of interest.</summary>
	public bool SearchPois
	{
		get => Read("searchPois", true);
		set => Write("searchPois", value);
	}

	/// <summary>"Use my location" starts at the address the device is at, not at the nearest stop.</summary>
	public bool ExactPosition
	{
		get => Read("exactPosition", false);
		set => Write("exactPosition", value);
	}

	/// <summary>The kinds of places a search for a journey endpoint may return.</summary>
	public PlaceKinds SearchKinds =>
		PlaceKinds.Stops
		| (SearchAddresses ? PlaceKinds.Addresses : PlaceKinds.None)
		| (SearchPois ? PlaceKinds.Pois : PlaceKinds.None);

	// ----- Start in input mode -----

	/// <summary>The app opens as if the passenger had tapped the start already: start filled, destination search ready.</summary>
	public bool StartInput
	{
		get => Read("startInput", false);
		set => Write("startInput", value);
	}

	/// <summary>What the start of the input mode is: the device position or a chosen place.</summary>
	public StartFromKind StartFrom
	{
		get => Read("startFrom", StartFromKind.Location);
		set => Write("startFrom", value);
	}

	/// <summary>The chosen start place (a stop, a point of interest or an address). Per provider: its ids mean nothing to another.</summary>
	public Location? StartFromPlace
	{
		get => LocationJson.FromJson(Read(Scoped("startFromPlace"), string.Empty));
		set => Write(Scoped("startFromPlace"), value is null ? string.Empty : LocationJson.ToJson(value));
	}

	// ----- Journey display -----

	/// <summary>Occupancy dots on stops and rides.</summary>
	public bool ShowOccupancy
	{
		get => Read("showOccupancy", true);
		set => Write("showOccupancy", value);
	}

	/// <summary>Steig/Gleis lines on stops and interchanges.</summary>
	public bool ShowPlatforms
	{
		get => Read("showPlatforms", true);
		set => Write("showPlatforms", value);
	}

	/// <summary>Open the intermediate stops of every ride when a journey is shown.</summary>
	public bool ExpandStops
	{
		get => Read("expandStops", false);
		set => Write("expandStops", value);
	}

	/// <summary>
	/// Shows the developer options (expert view, log file). Everything diagnostic is hidden and inactive
	/// while this is off.
	/// </summary>
	public bool DeveloperOptions
	{
		get => Read("developerOptions", false);
		set
		{
			Write("developerOptions", value);

			if (!value)
			{
				LogToFile = false;
			}
		}
	}

	/// <summary>Offer the expert view (raw provider data) in the journey menu; only with developer options.</summary>
	public bool ExpertView
	{
		get => DeveloperOptions && Read("expertView", true);
		set => Write("expertView", value);
	}

	/// <summary>Write provider traffic and diagnostics to the log file; only with developer options.</summary>
	public bool LogToFile
	{
		get => DeveloperOptions && Read("logToFile", false);
		set
		{
			Write("logToFile", value);
			DiagnosticLog.Enabled = value && DeveloperOptions;

			if (!DiagnosticLog.Enabled)
			{
				DiagnosticLog.Delete();
			}
		}
	}

	// ----- Map -----

	/// <summary>The user's CARTO API key for the basemap; empty switches the map off (see <see cref="MapAvailability"/>).</summary>
	public string MapApiKey
	{
		get => Read(MapAvailability.PreferenceKey, string.Empty).Trim();
		set
		{
			Write(MapAvailability.PreferenceKey, (value ?? string.Empty).Trim());
			MapAvailability.KeyChanged();
		}
	}

	// ----- Place search -----

	public const int MinSearchDelayMs = 200;
	public const int MaxSearchDelayMs = 1500;

	/// <summary>Quiet time after the last keystroke before places are searched, milliseconds.</summary>
	public int SearchDelayMs
	{
		get => Math.Clamp(Read("searchDelayMs", 500), MinSearchDelayMs, MaxSearchDelayMs);
		set => Write("searchDelayMs", Math.Clamp(value, MinSearchDelayMs, MaxSearchDelayMs));
	}

	/// <summary>VVO (and most other providers) answer HTTP 400 below three characters.</summary>
	public const int MinQueryLengthFloor = 3;
	public const int MinQueryLengthCeiling = 10;

	/// <summary>Characters needed before a place search starts (3..10).</summary>
	public int MinQueryLength
	{
		get => Math.Clamp(Read("minQueryLength", MinQueryLengthFloor), MinQueryLengthFloor, MinQueryLengthCeiling);
		set => Write("minQueryLength", Math.Clamp(value, MinQueryLengthFloor, MinQueryLengthCeiling));
	}

	// ----- Routing preferences -----
	// Kept per provider (what one provider supports or means by a mode says nothing about another). VVO keeps the
	// original keys, every other provider gets "<key>@<provider id>".

	private string Scoped(string key)
	{
		string provider = ProviderId;

		return string.IsNullOrWhiteSpace(provider)
			|| string.Equals(provider, VvoProviderInfo.Id, StringComparison.OrdinalIgnoreCase)
				? key
				: $"{key}@{provider}";
	}

	public const int MaxFootpathMinutes = 15;

	public MaxTransfers MaxTransfers
	{
		get => Read(Scoped("maxTransfers"), MaxTransfers.Unlimited);
		set => Write(Scoped("maxTransfers"), value);
	}

	public WalkingPace WalkingPace
	{
		get => Read(Scoped("walkingPace"), WalkingPace.Normal);
		set => Write(Scoped("walkingPace"), value);
	}

	/// <summary>Longest walk to an alternative stop, minutes (0..15).</summary>
	public int FootpathMinutes
	{
		get => Math.Clamp(Read(Scoped("footpathMinutes"), 5), 0, MaxFootpathMinutes);
		set => Write(Scoped("footpathMinutes"), Math.Clamp(value, 0, MaxFootpathMinutes));
	}

	public bool AlternativeStops
	{
		get => Read(Scoped("alternativeStops"), true);
		set => Write(Scoped("alternativeStops"), value);
	}

	/// <summary>Allowed modes of transport (never empty: an empty selection means all).</summary>
	public ModeFilter Modes
	{
		get
		{
			var modes = (ModeFilter)Read(Scoped("modes"), (int)ModeFilter.All) & ModeFilter.All;

			return modes == ModeFilter.None
				? ModeFilter.All
				: modes;
		}
		set => Write(Scoped("modes"), (int)(value & ModeFilter.All));
	}

	public AccessibilityNeed Accessibility
	{
		get => Read(Scoped("accessibility"), AccessibilityNeed.None);
		set => Write(Scoped("accessibility"), value);
	}

	public bool AvoidStairs
	{
		get => Read(Scoped("avoidStairs"), false);
		set => Write(Scoped("avoidStairs"), value);
	}

	public bool AvoidEscalators
	{
		get => Read(Scoped("avoidEscalators"), false);
		set => Write(Scoped("avoidEscalators"), value);
	}

	public bool FewestTransfers
	{
		get => Read(Scoped("fewestTransfers"), false);
		set => Write(Scoped("fewestTransfers"), value);
	}

	public RouteOptimisation Optimisation
	{
		get => Read(Scoped("optimisation"), RouteOptimisation.Fastest);
		set => Write(Scoped("optimisation"), value);
	}

	public EntranceNeed Entrance
	{
		get => Read(Scoped("entrance"), EntranceNeed.Any);
		set => Write(Scoped("entrance"), value);
	}

	/// <summary>Who the tickets are for (the price on cards and shares; asked of providers that quote by passenger).</summary>
	public PassengerCategory Passenger
	{
		get => Read(Scoped("passenger"), PassengerCategory.Adult);
		set => Write(Scoped("passenger"), value);
	}

	public ExtraChargeFilter ExtraCharge
	{
		get => Read(Scoped("extraCharge"), ExtraChargeFilter.Any);
		set => Write(Scoped("extraCharge"), value);
	}

	/// <summary>The routing options as sent with every journey search.</summary>
	public RoutingPreferences Routing =>
		new()
		{
			MaxTransfers = MaxTransfers,
			Pace = WalkingPace,
			FootpathMinutes = FootpathMinutes,
			AlternativeStops = AlternativeStops,
			Modes = Modes,
			Accessibility = Accessibility,
			AvoidStairs = AvoidStairs,
			AvoidEscalators = AvoidEscalators,
			FewestTransfers = FewestTransfers,
			Optimisation = Optimisation,
			Entrance = Entrance,
			ExtraCharge = ExtraCharge,
			Passenger = Passenger
		};

	public void ResetRoutingDefaults()
	{
		MaxTransfers = MaxTransfers.Unlimited;
		WalkingPace = WalkingPace.Normal;
		FootpathMinutes = 5;
		AlternativeStops = true;
		Modes = ModeFilter.All;
		Accessibility = AccessibilityNeed.None;
		AvoidStairs = false;
		AvoidEscalators = false;
		FewestTransfers = false;
		Optimisation = RouteOptimisation.Fastest;
		Entrance = EntranceNeed.Any;
		ExtraCharge = ExtraChargeFilter.Any;
		Passenger = PassengerCategory.Adult;
	}

	public void ResetJourneyDefaults()
	{
		MaxResults = 5;
		DefaultArrival = false;
		ShowWalkingLegs = true;
		ExpandNotices = false;
		TimeoutSeconds = 15;
		ShowOccupancy = true;
		SearchAddresses = true;
		SearchPois = true;
		ExactPosition = false;
		ShowPlatforms = true;
		ExpandStops = false;
		ExpertView = true;
		SearchDelayMs = 500;
		MinQueryLength = MinQueryLengthFloor;
		ResetRoutingDefaults();
	}

	// ----- plumbing -----

	private T Read<T>(string key, T fallback)
	{
		try
		{
			string raw = _prefs.Get(key, Convert.ToString(fallback, CultureInfo.InvariantCulture) ?? string.Empty);

			object? parsed = typeof(T) switch
			{
				var t when t == typeof(bool) => bool.TryParse(raw, out bool b) ? b : (object?)fallback,
				var t when t == typeof(int) => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : (object?)fallback,
				var t when t == typeof(string) => raw,
				var t when t.IsEnum => Enum.TryParse(t, raw, out object? e) && Enum.IsDefined(t, e!) ? e : (object?)fallback,
				_ => (object?)fallback
			};

			return (T)parsed!;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Settings read '{key}' failed: {ex.Message}");
			return fallback;
		}
	}

	private void Write<T>(string key, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
	{
		try
		{
			_prefs.Set(key, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
			Changed?.Invoke(this, name ?? key);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Settings write '{key}' failed: {ex.Message}");
		}
	}

	private static string NormalizeCulture(string languageCode) =>
	CultureInfo.GetCultureInfo(languageCode).Name;
}

/// <summary>The start of the input mode.</summary>
public enum StartFromKind
{
	/// <summary>The stop or address nearest to the device.</summary>
	Location = 0,

	/// <summary>A place chosen in the settings.</summary>
	Place
}
