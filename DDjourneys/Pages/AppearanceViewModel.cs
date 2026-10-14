using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Theming;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Mode, colour set, pure black and font face. Every choice applies immediately.</summary>
public sealed partial class AppearanceViewModel : DisposableViewModel
{
	private readonly LocalizationService _localization = LocalizationService.Current;
	private readonly AppSettings _settings;

	public AppearanceViewModel(AppSettings settings)
	{
		_settings = settings;

		AsyncCommand<string> selectMaterial = new(id => ApplyAsync(() => Theme.SetMaterialAsync(id ?? MaterialProfile.Mica)));
		AsyncCommand<string> selectMaterialSurfaces = new(id => ApplyAsync(() => Theme.SetMaterialSurfacesAsync(id ?? MaterialProfile.Layered)));

		AsyncCommand<string> selectMode = new(id => ApplyAsync(() => Theme.SetModeAsync(id ?? Theme.ModeSystem)));
		AsyncCommand<string> selectColor = new(id => ApplyAsync(() => Theme.SetColorAsync(id ?? ColorCatalog.DefaultId)));
		AsyncCommand<string> selectFont = new(id => ApplyAsync(() => Theme.SetFontAsync(id ?? FontCatalog.SystemId)));

		AsyncCommand<string> selectTextSize = new(id => ApplyAsync(() => Theme.SetTextSizeAsync(id ?? Core.Theming.TextSizeProfile.NormalId)));

		AsyncCommand<string> selectLineColours = new(
			id =>
			{
				ModeColorSet.Set(Application.Current ?? throw new InvalidOperationException("No application."), _settings, id ?? Core.Theming.ModeColorSets.StandardId);

				return Task.CompletedTask;
			});

		AsyncCommand<string> selectDensity = new(
			id =>
			{
				Density.Set(id ?? DensityProfile.NormalId);
				Refresh();

				return Task.CompletedTask;
			});

		Modes = Choices((string[])[Theme.ModeSystem, Theme.ModeLight, Theme.ModeDark, Theme.ModeSun], selectMode);
		Colors = Choices(ColorCatalog.All.Select(o => o.Id), selectColor);
		Fonts = Choices(FontCatalog.Ids, selectFont);
		Densities = Choices(DensityProfile.All.Select(d => d.Id), selectDensity);
		TextSizes = Choices(Core.Theming.TextSizeProfile.Ids, selectTextSize);
		LineColours = Choices(Core.Theming.ModeColorSets.Ids, selectLineColours);
		Materials = Choices(MaterialProfile.Materials.Where(Material.IsSupported), selectMaterial);
		MaterialSurfaces = Choices(MaterialProfile.Coverages, selectMaterialSurfaces);

		ListenToLocalization(_localization, OnLocalizationChanged);

		Subscribe(
			() =>
			{
				Theme.Changed += OnThemeChanged;
				Density.Changed += OnThemeChanged;
			},
			() =>
			{
				Theme.Changed -= OnThemeChanged;
				Density.Changed -= OnThemeChanged;
			});

		Refresh();
	}

	/// <summary>One card of choices: each row selects through the card's command; the last row draws no divider.</summary>
	private static List<AppearanceChoice> Choices(IEnumerable<string> ids, AsyncCommand<string> select)
	{
		List<AppearanceChoice> choices = [.. ids.Select(id => new AppearanceChoice(id, select))];

		if (choices.Count > 0)
		{
			choices[^1].ShowDivider = false;
		}

		return choices;
	}

	public List<AppearanceChoice> Modes { get; }

	public List<AppearanceChoice> Colors { get; }

	public List<AppearanceChoice> Fonts { get; }

	public List<AppearanceChoice> Densities { get; }

	/// <summary>The app-side text size (B11): normal, large, largest.</summary>
	public List<AppearanceChoice> TextSizes { get; }

	/// <summary>The line-colour set (B11): the standard colours or a colour-vision-safe remap.</summary>
	public List<AppearanceChoice> LineColours { get; }

	/// <summary>Window materials this device can show (Windows 11); none elsewhere.</summary>
	public List<AppearanceChoice> Materials { get; }

	public List<AppearanceChoice> MaterialSurfaces { get; }

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

	/// <summary>Journey cards reduced to the time and the next step (B11); one of the three settings of the preset.</summary>
	public bool SimpleCards
	{
		get => _settings.SimpleCards;

		set
		{
			if (value == _settings.SimpleCards)
			{
				return;
			}

			_settings.SimpleCards = value;

			OnPropertyChanged();
			OnPropertyChanged(nameof(LargeSimple));
		}
	}

	/// <summary>
	/// The "Large &amp; simple" preset (B11): applies largest text, the roomy density and the simple journey
	/// cards at once. A preset, not a fork: every one of the three settings stays adjustable on its own, and
	/// switching the preset off returns them to the normal combination.
	/// </summary>
	public bool LargeSimple
	{
		get =>
			Theme.TextSize == Core.Theming.TextSizeProfile.LargestId
			&& Density.Profile.Id == DensityProfile.TouchId
			&& _settings.SimpleCards;

		set
		{
			if (value == LargeSimple)
			{
				return;
			}

			string textSize = value ? Core.Theming.TextSizeProfile.LargestId : Core.Theming.TextSizeProfile.NormalId;
			string density = value ? DensityProfile.TouchId : DensityProfile.NormalId;

			_settings.SimpleCards = value;

			_ = ApplyAsync(
				async () =>
				{
					await Theme.SetTextSizeAsync(textSize);
					Density.Set(density);
				});
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
				Theme.ModeSun => s.ModeSunDescription,
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

		foreach (AppearanceChoice density in Densities)
		{
			(density.Title, density.Description) = density.Id switch
			{
				DensityProfile.CompactId => (s.DensityCompact, s.DensityCompactDescription),
				DensityProfile.TouchId => (s.DensityTouch, s.DensityTouchDescription),
				_ => (s.DensityNormal, s.DensityNormalDescription)
			};

			density.IsSelected = density.Id == Density.Profile.Id;
		}

		foreach (AppearanceChoice material in Materials)
		{
			(material.Title, material.Description) = material.Id switch
			{
				MaterialProfile.Mica => (s.MaterialMica, s.MaterialMicaDescription),
				MaterialProfile.MicaAlt => (s.MaterialMicaAlt, s.MaterialMicaAltDescription),
				MaterialProfile.Acrylic => (s.MaterialAcrylic, s.MaterialAcrylicDescription),
				_ => (s.MaterialNone, s.MaterialNoneDescription)
			};

			material.IsSelected = material.Id == Material.Id;
		}

		foreach (AppearanceChoice surfaces in MaterialSurfaces)
		{
			(surfaces.Title, surfaces.Description) = surfaces.Id switch
			{
				MaterialProfile.Backdrop => (s.SurfacesBackdrop, s.SurfacesBackdropDescription),
				MaterialProfile.Immersive => (s.SurfacesImmersive, s.SurfacesImmersiveDescription),
				_ => (s.SurfacesLayered, s.SurfacesLayeredDescription)
			};

			surfaces.IsSelected = surfaces.Id == Material.Surfaces;
		}

		foreach (AppearanceChoice size in TextSizes)
		{
			(size.Title, size.Description) = size.Id switch
			{
				Core.Theming.TextSizeProfile.LargeId => (s.TextSizeLarge, s.TextSizeLargeDescription),
				Core.Theming.TextSizeProfile.LargestId => (s.TextSizeLargest, s.TextSizeLargestDescription),
				_ => (s.TextSizeNormal, s.TextSizeNormalDescription)
			};

			size.IsSelected = size.Id == Theme.TextSize;
		}

		foreach (AppearanceChoice set in LineColours)
		{
			(set.Title, set.Description) = set.Id switch
			{
				Core.Theming.ModeColorSets.DeuteranopiaId => (s.LineColoursDeuteranopia, s.LineColoursDescription),
				Core.Theming.ModeColorSets.ProtanopiaId => (s.LineColoursProtanopia, s.LineColoursDescription),
				Core.Theming.ModeColorSets.TritanopiaId => (s.LineColoursTritanopia, s.LineColoursDescription),
				_ => (s.LineColoursStandard, s.LineColoursDescription)
			};

			set.IsSelected = set.Id == Core.Theming.ModeColorSets.IdOf(ModeColorSet.Current);
		}

		OnPropertyChanged(nameof(PureBlack));
		OnPropertyChanged(nameof(SimpleCards));
		OnPropertyChanged(nameof(LargeSimple));
	}

	private static bool IsGerman(LocalizationService localization) =>
		localization.LanguageCode.StartsWith("de", StringComparison.OrdinalIgnoreCase);

	private static string ModeTitle(string id, SettingsStrings s) =>
		id switch
		{
			Theme.ModeLight => s.ThemeLight,
			Theme.ModeDark => s.ThemeDark,
			Theme.ModeSun => s.ThemeSun,
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
			DiagnosticLog.Write($"Appearance change failed: {ex}");
		}
		finally
		{
			Refresh();
		}
	}
}
