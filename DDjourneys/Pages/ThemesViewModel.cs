using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>The full list of colour themes, grouped into light and dark.</summary>
public sealed class ThemesViewModel : ObservableObject
{
	private readonly LocalizationService _localization = LocalizationService.Current;

	public ThemesViewModel()
	{
		LightThemes = ThemeCatalog.All
			.Where(t => !t.IsDark)
			.Select(t => new ThemeOption(t.Id, t))
			.ToList();

		DarkThemes = ThemeCatalog.All
			.Where(t => t.IsDark)
			.Select(t => new ThemeOption(t.Id, t))
			.ToList();

		SelectThemeCommand = new AsyncCommand<string>(SelectThemeAsync);

		_localization.PropertyChanged += (_, _) =>
			MainThread.BeginInvokeOnMainThread(Refresh);

		Refresh();
	}

	public List<ThemeOption> LightThemes { get; }

	public List<ThemeOption> DarkThemes { get; }

	public AsyncCommand<string> SelectThemeCommand { get; }

	/// <summary>Updates names, descriptions and the checkmark.</summary>
	public void Refresh()
	{
		SettingsStrings strings = _localization.CurrentStrings.Settings;

		bool german = _localization.LanguageCode
			.StartsWith("de", StringComparison.OrdinalIgnoreCase);

		foreach (ThemeOption option in LightThemes.Concat(DarkThemes))
		{
			option.Update(Theme.Choice, strings, german);
		}
	}

	private async Task SelectThemeAsync(string? id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return;
		}

		try
		{
			await Theme.SetAsync(id);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Theme change failed: {ex}");
		}
		finally
		{
			Refresh();
		}
	}
}