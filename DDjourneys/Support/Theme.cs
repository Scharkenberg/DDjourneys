using DDjourneys.Core.Diagnostics;
using System.Runtime.CompilerServices;

namespace DDjourneys.Support;

/// <summary>
/// Applies the appearance: mode (system/light/dark), colour set and font face.
/// All values live as keyed entries in Application.Resources and are updated IN PLACE, so every
/// DynamicResource consumer is notified (replacing a whole merged dictionary does not reliably reach
/// everything that is already drawn). Two safeguards cover what in-place updates can miss:
/// pages that were not reached (e.g. further down the navigation stack) are re-validated when they appear
/// (<see cref="Revalidate"/>). State-dependent colours must use <see cref="Themed"/>, never DynamicResource
/// setters in VisualStates or Triggers (MAUI drops the style's registration when such a setter is unapplied).
/// Code that caches a theme colour should listen to <see cref="Changed"/>.
/// </summary>
public static class Theme
{
	public const string ModeSystem = "system";
	public const string ModeLight = "light";
	public const string ModeDark = "dark";

	private static readonly string[] BrushKeys = ["Outline", "Accent", "Surface", "Raised", "Ink", "InkMuted", "AccentSoft", "OnAccent"];

	// Everything currently written to Application.Resources by the theme (colours, brushes, fonts).
	private static readonly Dictionary<string, object> Current = new();

	// The palette as chosen, before a window material made parts of it translucent: what drawing code and
	// native chrome (status bar, share image) must use.
	private static readonly Dictionary<string, Color> Solid = new();
	private static readonly ConditionalWeakTable<Element, StampBox> Stamps = new();

	private static Application? _app;
	private static AppSettings? _settings;
	private static int _version;
	private static bool _applying;
	private static bool _materialDirty;

	public static event EventHandler? Changed;

	/// <summary>The page background of the palette in effect; the system bars wear it.</summary>
	public static Color BarColor =>
		Solid.TryGetValue("Bg", out Color? color) && color is not null
			? color
			: IsDark
				? Colors.Black
				: Colors.White;

	/// <summary>Whether the palette currently in effect is a dark one.</summary>
	public static bool IsDark { get; private set; }

	public static string Mode { get; private set; } = ModeSystem;

	public static string ColorId { get; private set; } = ColorCatalog.DefaultId;

	public static bool PureBlack { get; private set; } = true;

	public static string Font { get; private set; } = FontCatalog.OpenSansId;

	public static void Initialize(Application app, AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(settings);

		_app = app;
		_settings = settings;

		settings.MigrateAppearance();

		Mode = Normalize(settings.ThemeMode);
		ColorId = ColorCatalog.Find(settings.ThemeColor).Id;
		PureBlack = settings.ThemePureBlack;
		Font = FontCatalog.Normalize(settings.FontFace);
		Material.Initialize(settings);

		app.RequestedThemeChanged += (_, _) =>
		{
			if (Mode == ModeSystem)
			{
				Apply();
			}
		};

		Apply();
	}

	public static Task SetModeAsync(string mode)
	{
		string value = Normalize(mode);

		return ChangeAsync(
			() =>
			{
				if (value == Mode)
				{
					return false;
				}

				Mode = value;
				_settings?.ThemeMode = value;

				return true;
			});
	}

	public static Task SetColorAsync(string colorId)
	{
		string value = ColorCatalog.Find(colorId).Id;

		return ChangeAsync(
			() =>
			{
				if (value == ColorId)
				{
					return false;
				}

				ColorId = value;
				_settings?.ThemeColor = value;

				return true;
			});
	}

	public static Task SetPureBlackAsync(bool pureBlack) =>
		ChangeAsync(
			() =>
			{
				if (pureBlack == PureBlack)
				{
					return false;
				}

				PureBlack = pureBlack;
				_settings?.ThemePureBlack = pureBlack;

				return true;
			});

	/// <summary>Windows: the window material (none, mica, micaalt, acrylic).</summary>
	public static Task SetMaterialAsync(string materialId) =>
		ChangeAsync(() => Material.SetMaterial(materialId), notify: true);

	/// <summary>Windows: how far the window material reaches into the surfaces.</summary>
	public static Task SetMaterialSurfacesAsync(string surfacesId) =>
		ChangeAsync(() => Material.SetSurfaces(surfacesId), notify: true);

	public static Task SetFontAsync(string fontId)
	{
		string value = FontCatalog.Normalize(fontId);

		return ChangeAsync(
			() =>
			{
				if (value == Font)
				{
					return false;
				}

				Font = value;
				_settings?.FontFace = value;

				return true;
			});
	}

	/// <summary>A colour of the palette currently in effect (for code that draws outside the view tree).</summary>
	public static Color ColorOf(string key, Color fallback) =>
		Solid.TryGetValue(key, out Color? color) && color is not null
			? color
			: Current.TryGetValue(key, out object? value) && value is Color current ? current : fallback;

	/// <summary>Re-reads OS-dependent inputs (system accent, OS dark mode). Cheap when nothing changed.</summary>
	public static void Refresh() => Apply();

	/// <summary>
	/// Makes sure a page that is about to be shown reflects the current theme, even if it was not reached
	/// when the theme changed. Call from OnAppearing.
	/// </summary>
	public static void Revalidate(Page page)
	{
		ArgumentNullException.ThrowIfNull(page);

		if (Stamps.TryGetValue(page, out StampBox? box) && box.Version == _version)
		{
			return;
		}

		Stamps.Remove(page);
		Stamps.Add(page, new StampBox(_version));

		if (Current.Count == 0)
		{
			return;
		}

		try
		{
			// Writing a key into the page's own dictionary notifies every DynamicResource below the page;
			// removing it again lets the page keep following the application-level value afterwards.
			ResourceDictionary resources = page.Resources;

			foreach ((string key, object value) in Current)
			{
				resources[key] = value;
			}

			foreach (string key in Current.Keys)
			{
				resources.Remove(key);
			}

		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Theme revalidate failed: {ex.Message}");
		}
	}

	private static string Normalize(string? mode) =>
		mode?.ToLowerInvariant() switch
		{
			ModeLight => ModeLight,
			ModeDark => ModeDark,
			_ => ModeSystem
		};

	private static async Task ChangeAsync(Func<bool> mutate, bool notify = false)
	{
		if (!mutate())
		{
			return;
		}

		// The window material itself changes without a colour change (Mica to Mica Alt): tell the platform.
		_materialDirty |= notify;

		VisualElement? page = null;
		bool animate = _settings?.Animations ?? false;

		try
		{
			page = Shell.Current?.CurrentPage;

			if (animate && page is not null)
			{
				await page.FadeToAsync(0.0, 90, Easing.CubicIn);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Theme fade-out skipped: {ex.Message}");
		}

		try
		{
			Apply();
		}
		finally
		{
			try
			{
				if (page is not null)
				{
					page.Opacity = 0;
					await page.FadeToAsync(1.0, 180, Easing.CubicOut);
				}
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write($"Theme fade-in skipped: {ex.Message}");

				if (page is not null)
				{
					page.Opacity = 1;
				}
			}
		}
	}

	private static void Apply()
	{
		if (_app is null || _applying)
		{
			return;
		}

		_applying = true;

		try
		{
			// 1. Native chrome (status bar, dialogs, default controls) follows Light/Dark.
			//    "system" hands control back to the OS by clearing UserAppTheme.
			//    This must happen BEFORE the mode is resolved: RequestedTheme keeps
			//    returning the previously forced mode until it is cleared.
			AppTheme native = Mode switch
			{
				ModeLight => AppTheme.Light,
				ModeDark => AppTheme.Dark,
				_ => AppTheme.Unspecified
			};

			if (_app.UserAppTheme != native)
			{
				_app.UserAppTheme = native;
			}

			// PlatformAppTheme ignores UserAppTheme, so it always reports the OS.
			bool dark = Mode switch
			{
				ModeLight => false,
				ModeDark => true,
				_ => _app.PlatformAppTheme == AppTheme.Dark
			};

			// 2. Colours and fonts, written in place; only entries that differ are touched.
			//    A window material (Windows 11) makes background and surfaces translucent layers.
			bool materialWas = Material.Active;
			Material.Resolve(dark, PureBlack);

			ResourceDictionary resources = _app.Resources;
			bool changed = materialWas != Material.Active || _materialDirty;

			foreach ((string key, Color solid) in ColorCatalog.Resolve(ColorId, dark, PureBlack))
			{
				Solid[key] = solid;

				Color color = Material.Tint(key, solid, dark);

				if (Current.TryGetValue(key, out object? old) && old is Color oldColor && oldColor == color)
				{
					continue;
				}

				changed = true;
				Current[key] = color;
				resources[key] = color;

				if (BrushKeys.Contains(key))
				{
					var brush = new SolidColorBrush(color);
					Current[key + "Brush"] = brush;
					resources[key + "Brush"] = brush;
				}
			}

			(string regular, string semibold) = FontCatalog.Families(Font);

			changed |= SetText(resources, "FontRegular", regular);
			changed |= SetText(resources, "FontSemibold", semibold);

			bool darkChanged = IsDark != dark;
			IsDark = dark;

			if (!changed && !darkChanged)
			{
				return;
			}

			_version++;

			if (_materialDirty || materialWas != Material.Active)
			{
				_materialDirty = false;
				Material.Raise();
			}

#if ANDROID
			// System bars: the palette's background, with icons that stay legible on this theme.
			// The activity may not exist yet (first call during app start); MainActivity repeats this.
			DDjourneys.Platforms.Android.SystemBars.Apply(
				Microsoft.Maui.ApplicationModel.Platform.CurrentActivity,
				dark);
#endif

			foreach (Window window in _app.Windows)
			{
				if (window.Page is { } root)
				{
					Stamps.Remove(root);
					Stamps.Add(root, new StampBox(_version));
				}
			}

			Changed?.Invoke(null, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Theme apply failed: {ex}");
		}
		finally
		{
			_applying = false;
		}
	}

	private static bool SetText(ResourceDictionary resources, string key, string value)
	{
		if (Current.TryGetValue(key, out object? old) && old is string text && text == value)
		{
			return false;
		}

		Current[key] = value;
		resources[key] = value;

		return true;
	}

	private sealed class StampBox(int version)
	{
		public int Version { get; } = version;
	}
}
