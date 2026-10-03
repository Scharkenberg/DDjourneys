using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Mode, colour set, pure black and font face. Every choice applies immediately.</summary>
public sealed class AppearanceViewModel : DisposableViewModel
{
	private readonly LocalizationService _localization = LocalizationService.Current;

	public AppearanceViewModel()
	{
		Modes =
		[
			new(Theme.ModeSystem, false),
			new(Theme.ModeLight, false),
			new(Theme.ModeDark, false)
		];

		Colors = ColorCatalog.All.Select(o => new AppearanceChoice(o.Id, true)).ToList();

		Fonts = FontCatalog.Ids.Select(id => new AppearanceChoice(id, false)).ToList();

		SelectModeCommand = new AsyncCommand<string>(id => ApplyAsync(() => Theme.SetModeAsync(id ?? Theme.ModeSystem)));
		SelectColorCommand = new AsyncCommand<string>(id => ApplyAsync(() => Theme.SetColorAsync(id ?? ColorCatalog.DefaultId)));
		SelectFontCommand = new AsyncCommand<string>(id => ApplyAsync(() => Theme.SetFontAsync(id ?? FontCatalog.OpenSansId)));

		ListenToLocalization(_localization, OnLocalizationChanged);

		Subscribe(
			() => Theme.Changed += OnThemeChanged,
			() => Theme.Changed -= OnThemeChanged);

		Refresh();
	}

	public List<AppearanceChoice> Modes { get; }

	public List<AppearanceChoice> Colors { get; }

	public List<AppearanceChoice> Fonts { get; }

	public AsyncCommand<string> SelectModeCommand { get; }

	public AsyncCommand<string> SelectColorCommand { get; }

	public AsyncCommand<string> SelectFontCommand { get; }

	/// <summary>Pure black surfaces in dark mode (two-way bound to a switch).</summary>
	public bool PureBlack
	{
		get => Theme.PureBlack;
		set
		{
			if (value == Theme.PureBlack)
			{
				return;
			}

			_ = ApplyAsync(() => Theme.SetPureBlackAsync(value));
		}
	}

	/// <summary>One line for the settings page, for example "System · Default · Open Sans".</summary>
	public static string Summary(LocalizationService localization)
	{
		SettingsStrings s = localization.CurrentStrings.Settings;
		bool german = IsGerman(localization);
		ColorOption color = ColorCatalog.Find(Theme.ColorId);

		return string.Join(" · ", ModeTitle(Theme.Mode, s), ColorTitle(color, s, german), FontTitle(Theme.Font, s));
	}

	/// <summary>Re-resolves texts, swatches and selection marks.</summary>
	public void Refresh()
	{
		SettingsStrings s = _localization.CurrentStrings.Settings;
		bool german = IsGerman(_localization);

		foreach (AppearanceChoice mode in Modes)
		{
			mode.Title = ModeTitle(mode.Id, s);

			mode.Description = mode.Id switch
			{
				Theme.ModeLight => s.ModeLightDescription,
				Theme.ModeDark => s.ModeDarkDescription,
				_ => s.ModeSystemDescription
			};

			mode.IsSelected = mode.Id == Theme.Mode;
		}

		foreach (AppearanceChoice choice in Colors)
		{
			ColorOption option = ColorCatalog.Find(choice.Id);
			choice.Title = ColorTitle(option, s, german);
			choice.Description = ColorDescription(option, s, german);
			choice.IsSelected = option.Id == Theme.ColorId;
			choice.SetSwatch(ColorCatalog.Resolve(option.Id, Theme.IsDark, Theme.PureBlack));
		}

		foreach (AppearanceChoice font in Fonts)
		{
			font.Title = FontTitle(font.Id, s);

			font.Description = font.Id switch
			{
				FontCatalog.InterTightId => s.FontInterTightDescription,
				FontCatalog.SystemId => s.FontSystemDescription,
				_ => s.FontOpenSansDescription
			};

			font.IsSelected = font.Id == Theme.Font;
		}

		OnPropertyChanged(nameof(PureBlack));
	}

	private static bool IsGerman(LocalizationService localization) =>
		localization.LanguageCode.StartsWith("de", StringComparison.OrdinalIgnoreCase);

	private static string ModeTitle(string id, SettingsStrings s) =>
		id switch
		{
			Theme.ModeLight => s.ThemeLight,
			Theme.ModeDark => s.ThemeDark,
			_ => s.ThemeSystem
		};

	private static string ColorTitle(ColorOption o, SettingsStrings s, bool german) =>
		o.Id switch
		{
			ColorCatalog.SystemId => s.ColorSystem,
			ColorCatalog.SystemSolarizedId => s.ColorSystemSolarized,
			_ => german ? o.NameDe : o.Name
		};

	private static string ColorDescription(ColorOption o, SettingsStrings s, bool german) =>
		o.Id switch
		{
			ColorCatalog.SystemId => s.ColorSystemDescription,
			ColorCatalog.SystemSolarizedId => s.ColorSystemSolarizedDescription,
			_ => german ? o.DescriptionDe : o.Description
		};

	private static string FontTitle(string id, SettingsStrings s) =>
		id switch
		{
			FontCatalog.InterTightId => s.FontInterTight,
			FontCatalog.SystemId => s.FontSystem,
			_ => s.FontOpenSans
		};

	private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
		Post(Refresh);

	private void OnThemeChanged(object? sender, EventArgs e) => Post(Refresh);

	private void Post(Action action) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					action();
				}
			});

	private async Task ApplyAsync(Func<Task> change)
	{
		try
		{
			await change();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Appearance change failed: {ex}");
		}
		finally
		{
			Refresh();
		}
	}
}
