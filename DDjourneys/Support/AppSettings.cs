using System.Globalization;

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

	/// <summary>Theme id from <see cref="ThemeCatalog"/>, or "system".</summary>
	public string ThemeId
	{
		get
		{
			string id = Read("themeId", string.Empty);

			// Older versions stored the enum name under "theme".
			return id.Length > 0
				? id
				: Read("theme", "System").ToLowerInvariant();
		}
		set => Write("themeId", value);
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

	/// <summary>Request timeout for provider calls, seconds (5..60).</summary>
	public int TimeoutSeconds
	{
		get => Math.Clamp(Read("timeout", 15), 5, 60);
		set => Write("timeout", Math.Clamp(value, 5, 60));
	}

	public void ResetJourneyDefaults()
	{
		MaxResults = 5;
		DefaultArrival = false;
		ShowWalkingLegs = true;
		ExpandNotices = false;
		TimeoutSeconds = 15;
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
			System.Diagnostics.Debug.WriteLine($"Settings read '{key}' failed: {ex.Message}");
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
			System.Diagnostics.Debug.WriteLine($"Settings write '{key}' failed: {ex.Message}");
		}
	}

	private static string NormalizeCulture(string languageCode) =>
	CultureInfo.GetCultureInfo(languageCode).Name;
}