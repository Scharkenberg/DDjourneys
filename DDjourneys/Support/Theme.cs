using DDjourneys.Core.Diagnostics;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
	private static readonly Dictionary<string, object> Current = [];

	// The palette as chosen, before a window material made parts of it translucent: what drawing code and
	// native chrome (status bar, share image) must use.
	private static readonly Dictionary<string, Color> Solid = [];
	private static readonly ConditionalWeakTable<Element, StampBox> Stamps = [];

	// Roots of view trees that the window's own tree does not reach (lent pages' slots, pages that were built but are
	// not shown, popups). The in-place update of Application.Resources notifies only what hangs under a window.
	private static readonly List<WeakReference<VisualElement>> Tracked = [];

	// Values every key had before: only an element that still shows one of them is stale. An element whose value comes
	// from somewhere else (a binding, a local value: the colours of a line chip) is none of the repaint's business.
	private static readonly Dictionary<string, HashSet<object>> Retired = [];

	// Keys whose value changed in the latest Apply: all a sweep has to announce.
	private static readonly HashSet<string> ChangedKeys = [];

	private static readonly FieldInfo? RegistrationsField = FindRegistrations();

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

	public static string Font { get; private set; } = FontCatalog.SystemId;

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
	/// Registers the root of a view tree that can be outside the window's own tree (a lent page's slot, a popup, a page
	/// that is built but not shown). Every theme change then announces the new values to it directly.
	/// </summary>
	public static void Track(VisualElement root)
	{
		ArgumentNullException.ThrowIfNull(root);

		lock (Tracked)
		{
			Tracked.RemoveAll(item => !item.TryGetTarget(out VisualElement? live) || ReferenceEquals(live, root));
			Tracked.Add(new WeakReference<VisualElement>(root));
		}
	}

	/// <summary>
	/// Makes sure a tree that is about to be shown reflects the current theme, even if it was not reached when the theme
	/// changed. Call from OnAppearing (pages) or when a lent slot is built. Cheap when it is up to date.
	/// </summary>
	public static void Revalidate(VisualElement root)
	{
		ArgumentNullException.ThrowIfNull(root);

		Track(root);

		if (Stamps.TryGetValue(root, out StampBox? box) && box.Version == _version)
		{
			return;
		}

		Stamps.Remove(root);
		Stamps.Add(root, new StampBox(_version));

		Announce(root, Current.Keys);
		Repaint(root, []);
	}

	/// <summary>
	/// After a change: every tree this app owns hears the keys that changed, whether or not the window's tree reaches it
	/// (shell, pages on the stack, modal pages, lent slots, popups).
	/// </summary>
	private static void Sweep()
	{
		if (_app is null)
		{
			ChangedKeys.Clear();

			return;
		}

		string[] keys = [.. ChangedKeys];
		ChangedKeys.Clear();

		if (keys.Length == 0)
		{
			return;
		}

		var roots = new List<VisualElement>();

		foreach (Window window in _app.Windows)
		{
			if (window.Page is not { } root)
			{
				continue;
			}

			roots.Add(root);

			if (root is Shell shell)
			{
				AddPages(roots, shell.Navigation.NavigationStack);
				AddPages(roots, shell.Navigation.ModalStack);
			}
			else if (root.Navigation is { } navigation)
			{
				AddPages(roots, navigation.NavigationStack);
				AddPages(roots, navigation.ModalStack);
			}
		}

		lock (Tracked)
		{
			Tracked.RemoveAll(item => !item.TryGetTarget(out _));

			foreach (WeakReference<VisualElement> item in Tracked)
			{
				if (item.TryGetTarget(out VisualElement? live))
				{
					roots.Add(live);
				}
			}
		}

		var visited = new HashSet<Element>();
		int repaired = 0;

		foreach (VisualElement root in roots.Distinct())
		{
			Stamps.Remove(root);
			Stamps.Add(root, new StampBox(_version));

			Announce(root, keys);
			repaired += Repaint(root, visited);
		}

		DiagnosticLog.Write($"[Theme] swept {visited.Count} elements in {roots.Count} roots, {repaired} stale registrations repaired");
	}

	/// <summary>
	/// The safety net under the announcement: whatever the propagation missed (a registration that kept an old value
	/// for any reason) is found by comparing each element's theme-bound properties with the palette in effect and is
	/// registered again, which resolves the current value at once. Only elements that really differ are touched, so
	/// nothing that is up to date changes its registration. Without the internal list (a future MAUI) it does nothing.
	/// </summary>
	private static int Repaint(Element element, HashSet<Element> visited)
	{
		if (!visited.Add(element))
		{
			return 0;
		}

		int repaired = 0;

		try
		{
			if (RegistrationsField?.GetValue(element) is IDictionary map && map.Count > 0)
			{
				// A copy: registering again changes the dictionary. The generic dictionary yields key/value pairs
				// through the non-generic interface's enumerator, so read it through IDictionaryEnumerator.
				List<DictionaryEntry> entries = [];
				IDictionaryEnumerator cursor = map.GetEnumerator();

				while (cursor.MoveNext())
				{
					entries.Add(cursor.Entry);
				}

				foreach (DictionaryEntry entry in entries)
				{
					if (entry.Key is BindableProperty property
						&& entry.Value is ITuple { Length: > 0 } pair
						&& pair[0] is string key
						&& Current.TryGetValue(key, out object? wanted)
						&& element.GetValue(property) is { } shown
						&& !IsCurrent(shown, wanted)
						&& Retired.TryGetValue(key, out HashSet<object>? old)
						&& Normalize(shown) is { } plain
						&& old.Contains(plain))
					{
						element.SetDynamicResource(property, key);
						repaired++;
					}
				}
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Theme] repaint skipped on {element.GetType().Name}: {ex.Message}");
		}

		if (element is IVisualTreeElement tree)
		{
			foreach (IVisualTreeElement child in tree.GetVisualChildren())
			{
				if (child is Element next)
				{
					repaired += Repaint(next, visited);
				}
			}
		}

		return repaired;
	}

	private static void Retire(string key, object? old)
	{
		if (Normalize(old) is not { } value)
		{
			return;
		}

		if (!Retired.TryGetValue(key, out HashSet<object>? set))
		{
			Retired[key] = set = [];
		}

		if (set.Count > 48)
		{
			set.Clear();
		}

		set.Add(value);
	}

	private static object? Normalize(object? value) =>
		value is SolidColorBrush brush ? brush.Color : value;

	private static bool IsCurrent(object? now, object wanted) =>
		Equals(now, wanted)
		|| (now is SolidColorBrush solid && wanted is Color color && solid.Color == color)
		|| (now is Color flat && wanted is SolidColorBrush brush && brush.Color == flat);

	[DynamicDependency("_dynamicResources", typeof(Element))]
	private static FieldInfo? FindRegistrations() =>
		typeof(Element).GetField("_dynamicResources", BindingFlags.Instance | BindingFlags.NonPublic);

	private static void AddPages(List<VisualElement> roots, IEnumerable<Page?> pages)
	{
		foreach (Page? page in pages)
		{
			if (page is not null)
			{
				roots.Add(page);
			}
		}
	}

	/// <summary>
	/// Writing a key into the root's own dictionary notifies every DynamicResource below it; removing it again lets the
	/// tree keep following the application-level value afterwards. A key the root defines itself is left alone.
	/// </summary>
	private static void Announce(VisualElement root, IEnumerable<string> keys)
	{
		if (Current.Count == 0)
		{
			return;
		}

		try
		{
			ResourceDictionary resources = root.Resources;
			List<string> written = [];

			foreach (string key in keys)
			{
				if (Current.TryGetValue(key, out object? value) && !resources.ContainsKey(key))
				{
					resources[key] = value;
					written.Add(key);
				}
			}

			foreach (string key in written)
			{
				resources.Remove(key);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Theme announce failed: {ex.Message}");
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

				page?.Opacity = 1;
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
				ChangedKeys.Add(key);
				Retire(key, old);
				Current[key] = color;
				resources[key] = color;

				if (BrushKeys.Contains(key))
				{
					var brush = new SolidColorBrush(color);
					Retire(key + "Brush", old is Color before ? new SolidColorBrush(before) : null);
					Current[key + "Brush"] = brush;
					ChangedKeys.Add(key + "Brush");
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

			Sweep();

			Changed?.Invoke(null, EventArgs.Empty);
			UiDiagnostics.ThemeChanged();
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

		Retire(key, old);
		Current[key] = value;
		ChangedKeys.Add(key);
		resources[key] = value;

		return true;
	}

	private sealed class StampBox(int version)
	{
		public int Version { get; } = version;
	}
}
