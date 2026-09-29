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
/// </summary>
public static class Theme
{
	private const string PreferenceKey = "theme";

	private static Application? _app;
	private static ResourceDictionary? _current;

	public static ThemeChoice Choice { get; private set; } = ThemeChoice.System;

	public static void Initialize(Application app)
	{
		_app = app;

		Choice = Enum.TryParse(
			Preferences.Get(PreferenceKey, nameof(ThemeChoice.System)),
			out ThemeChoice saved)
				? saved
				: ThemeChoice.System;

		app.RequestedThemeChanged += (_, _) =>
		{
			if (Choice == ThemeChoice.System)
			{
				Apply();
			}
		};

		Apply();
	}

	public static void Set(ThemeChoice choice)
	{
		Choice = choice;
		Preferences.Set(PreferenceKey, choice.ToString());
		Apply();
	}

	private static void Apply()
	{
		if (_app is null)
		{
			return;
		}

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
			_ => _app.RequestedTheme == AppTheme.Dark
				? new ThemeAmoled()
				: new ThemeLight()
		};

		var merged = _app.Resources.MergedDictionaries;

		if (_current is not null)
		{
			merged.Remove(_current);
		}

		merged.Add(next);
		_current = next;
	}
}
