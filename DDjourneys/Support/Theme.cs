namespace DDjourneys.Support;

/// <summary>
/// Applies the chosen theme by swapping one merged ResourceDictionary built from a
/// <see cref="ThemeDef"/> (AppThemeBinding only knows Light and Dark, so it cannot
/// express many themes). "system" follows the OS: light stays Light, dark becomes AMOLED.
/// Brush twins ("OutlineBrush", "AccentBrush", ...) are generated from the colours,
/// so theme files only ever declare colours.
/// </summary>
public static class Theme
{
	private static readonly string[] BrushKeys = ["Outline", "Accent", "Surface", "Raised", "Ink", "InkMuted", "AccentSoft"];

	private static Application? _app;
	private static AppSettings? _settings;
	private static ResourceDictionary? _current;

	/// <summary>Id of the chosen theme, or "system".</summary>
	public static string Choice { get; private set; } = ThemeCatalog.SystemId;

	public static void Initialize(Application app, AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(settings);

		_app = app;
		_settings = settings;
		Choice = settings.ThemeId;

		app.RequestedThemeChanged += (_, _) =>
		{
			if (Choice == ThemeCatalog.SystemId)
			{
				Apply();
			}
		};

		Apply();
	}

	/// <summary>Sets, persists and applies a theme, cross-fading the visible page.</summary>
	public static async Task SetAsync(string choice)
	{
		if (string.Equals(choice, Choice, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		Choice = choice;

		if (_settings is not null)
		{
			_settings.ThemeId = choice;
		}

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
			System.Diagnostics.Debug.WriteLine($"Theme fade-out skipped: {ex.Message}");
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
				System.Diagnostics.Debug.WriteLine($"Theme fade-in skipped: {ex.Message}");
				if (page is not null)
				{
					page.Opacity = 1;
				}
			}
		}
	}

	private static void Apply()
	{
		if (_app is null)
		{
			return;
		}

		try
		{
			// 1. Native chrome (status bar, dialogs, default controls) follows Light/Dark.
			//    "system" hands control back to the OS by clearing UserAppTheme.
			//    This must happen BEFORE the palette is resolved: RequestedTheme
			//    keeps returning the previously forced mode until it is cleared.
			bool system = Choice == ThemeCatalog.SystemId;
			ThemeDef chosen = system ? ThemeCatalog.Find(ThemeCatalog.LightId) : ThemeCatalog.Find(Choice);

			AppTheme native =
				system
					? AppTheme.Unspecified
					: chosen.IsDark ? AppTheme.Dark : AppTheme.Light;

			if (_app.UserAppTheme != native)
			{
				_app.UserAppTheme = native;
			}

			// 2. The palette. For "system" ask the OS directly (PlatformAppTheme
			//    ignores UserAppTheme): light stays Light, dark becomes AMOLED.
			ThemeDef def =
				system
					? ThemeCatalog.Find(
						_app.PlatformAppTheme == AppTheme.Dark
							? ThemeCatalog.AmoledId
							: ThemeCatalog.LightId)
					: chosen;

			var next = new ResourceDictionary();

			foreach ((string key, Color color) in def.Palette)
			{
				next[key] = color;

				if (BrushKeys.Contains(key))
				{
					next[key + "Brush"] = new SolidColorBrush(color);
				}
			}

			var merged = _app.Resources.MergedDictionaries;

			if (_current is not null)
			{
				merged.Remove(_current);
			}

			merged.Add(next);
			_current = next;

#if ANDROID
			// Status bar: same colour as the page background (so it reads as transparent
			// and follows every theme) and icons that stay legible on it.
			// Android 15+ draws edge-to-edge anyway; older versions honour this call.
			CommunityToolkit.Maui.Core.Platform.StatusBar.SetColor(def.Bg);
			CommunityToolkit.Maui.Core.Platform.StatusBar.SetStyle(
				def.IsDark
					? CommunityToolkit.Maui.Core.StatusBarStyle.LightContent
					: CommunityToolkit.Maui.Core.StatusBarStyle.DarkContent);
#endif
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Theme apply failed: {ex}");
		}
	}
}