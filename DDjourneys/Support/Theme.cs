namespace DDjourneys.Support;

public enum ThemeChoice
{
	System,
	Light,
	Dark,
	Amoled
}

/// <summary>
/// Applies the chosen theme by swapping one merged ResourceDictionary.
/// (AppThemeBinding only knows Light and Dark, so it cannot express three themes.)
/// System follows the OS: light stays Light, dark becomes AMOLED.
/// Brush twins ("OutlineBrush", "AccentBrush", ...) are generated from the colours,
/// so theme files only ever declare colours.
/// </summary>
public static class Theme
{
	private static readonly string[] BrushKeys = ["Outline", "Accent", "Surface", "Raised", "Ink", "InkMuted", "AccentSoft"];

	private static Application? _app;
	private static AppSettings? _settings;
	private static ResourceDictionary? _current;

	public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;

	public static void Initialize(Application app, AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(settings);

		_app = app;
		_settings = settings;
		Choice = settings.Theme;

		app.RequestedThemeChanged += (_, _) =>
		{
			if (Choice == ThemeChoice.System)
			{
				Apply();
			}
		};

		Apply();
	}

	/// <summary>Sets, persists and applies a theme, cross-fading the visible page.</summary>
	public static async Task SetAsync(ThemeChoice choice)
	{
		if (choice == Choice)
		{
			return;
		}

		Choice = choice;

		if (_settings is not null)
		{
			_settings.Theme = choice;
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
			// Native chrome (status bar, dialogs, default controls) follows Light/Dark.
			AppTheme native = Choice switch
			{
				ThemeChoice.Light => AppTheme.Light,
				ThemeChoice.Dark or ThemeChoice.Amoled => AppTheme.Dark,
				_ => AppTheme.Unspecified
			};

			if (_app.UserAppTheme != native)
			{
				_app.UserAppTheme = native;
			}

			ResourceDictionary next = Choice switch
			{
				ThemeChoice.Light => new ThemeLight(),
				ThemeChoice.Dark => new ThemeDark(),
				ThemeChoice.Amoled => new ThemeAmoled(),
				_ => _app.RequestedTheme == AppTheme.Dark ? new ThemeAmoled() : new ThemeLight()
			};

			foreach (string key in BrushKeys)
			{
				if (next.TryGetValue(key, out object? value) && value is Color color)
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
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Theme apply failed: {ex}");
		}
	}
}
