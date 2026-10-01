using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Thin, two-way view over <see cref="AppSettings"/>. Persisting is AppSettings' job.</summary>
public sealed class SettingsViewModel : ObservableObject
{
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	public SettingsViewModel(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
		_localization = LocalizationService.Current;

		_localization.PropertyChanged += OnLocalizationChanged;

		SelectSystemThemeCommand = new AsyncCommand(SelectSystemThemeAsync);
		OpenThemesCommand = new AsyncCommand(OpenThemesAsync);
		SelectLanguageCommand = new Command<string>(SelectLanguage);
		ResetCommand = new Command(Reset);
	}

	public AsyncCommand SelectSystemThemeCommand { get; }
	public AsyncCommand OpenThemesCommand { get; }
	public Command<string> SelectLanguageCommand { get; }
	public Command ResetCommand { get; }

	// ----- Localization -----

	public IReadOnlyList<LocalizationPack> AvailableLanguages =>
		_localization.AvailableLanguages;

	public string LanguageCode =>
		_localization.LanguageCode;

	// ----- Appearance -----

	public bool Animations
	{
		get => _settings.Animations;
		set
		{
			_settings.Animations = value;
			OnPropertyChanged();
		}
	}

	public bool ShowTechnicalDetails
	{
		get => _settings.ShowTechnicalDetails;
		set
		{
			_settings.ShowTechnicalDetails = value;
			OnPropertyChanged();
		}
	}

	// ----- Journeys -----

	public double MaxResults
	{
		get => _settings.MaxResults;
		set
		{
			int rounded = (int)Math.Round(value);

			// The slider reports fractional values while dragging; only a
			// whole-step change is stored. Always notify so the slider snaps.
			if (rounded != _settings.MaxResults)
			{
				_settings.MaxResults = rounded;
			}

			OnPropertyChanged();
			OnPropertyChanged(nameof(MaxResultsText));
		}
	}

	public string MaxResultsText =>
		$"{_settings.MaxResults} " +
		_localization.CurrentStrings.Settings.ResultsDescription.ToLowerInvariant();

	public double TimeoutSeconds
	{
		get => _settings.TimeoutSeconds;
		set
		{
			int stepped = (int)Math.Round(value / 5.0) * 5;

			if (stepped != _settings.TimeoutSeconds)
			{
				_settings.TimeoutSeconds = stepped;
			}

			OnPropertyChanged();
			OnPropertyChanged(nameof(TimeoutText));
		}
	}

	public string TimeoutText =>
		string.Format(
			_localization.CurrentStrings.Settings.RequestTimeoutDescription,
			_settings.TimeoutSeconds);

	public double MinResults => AppSettings.MinResults;

	public double MaxResultsLimit => AppSettings.MaxResultsLimit;

	public bool DefaultArrival
	{
		get => _settings.DefaultArrival;
		set
		{
			_settings.DefaultArrival = value;
			OnPropertyChanged();
		}
	}

	public bool ShowWalkingLegs
	{
		get => _settings.ShowWalkingLegs;
		set
		{
			_settings.ShowWalkingLegs = value;
			OnPropertyChanged();
		}
	}

	public bool ExpandNotices
	{
		get => _settings.ExpandNotices;
		set
		{
			_settings.ExpandNotices = value;
			OnPropertyChanged();
		}
	}

	public string Version
	{
		get
		{
			try
			{
				return string.Format(
					_localization.CurrentStrings.Settings.VersionPrefix,
					AppInfo.Current.VersionString,
					AppInfo.Current.BuildString);
			}
			catch (Exception)
			{
				return "DDjourneys";
			}
		}
	}

	// ----- Theme: only "System" or "Other" is chosen here -----

	public bool IsSystemTheme =>
		Theme.Choice == ThemeCatalog.SystemId;

	public bool IsOtherTheme => !IsSystemTheme;

	/// <summary>Shows which theme "Other" currently stands for.</summary>
	public string OtherThemeText
	{
		get
		{
			SettingsStrings strings = _localization.CurrentStrings.Settings;

			return IsSystemTheme
				? strings.ThemeOtherDescription
				: ThemeOption.NameOf(ThemeCatalog.Find(Theme.Choice), strings);
		}
	}

	/// <summary>Called when the page (re)appears, e.g. after returning from the theme list.</summary>
	public void RefreshTheme()
	{
		OnPropertyChanged(nameof(IsSystemTheme));
		OnPropertyChanged(nameof(IsOtherTheme));
		OnPropertyChanged(nameof(OtherThemeText));
	}

	private async Task SelectSystemThemeAsync()
	{
		try
		{
			await Theme.SetAsync(ThemeCatalog.SystemId);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Theme change failed: {ex}");
		}
		finally
		{
			RefreshTheme();
		}
	}

	private async Task OpenThemesAsync()
	{
		try
		{
			await Shell.Current.GoToAsync(Routes.Themes);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Opening themes failed: {ex}");
		}
	}

	private void SelectLanguage(string? languageCode)
	{
		if (string.IsNullOrWhiteSpace(languageCode))
		{
			return;
		}

		try
		{
			_localization.SetLanguage(languageCode);
			_settings.LanguageCode = _localization.LanguageCode;
			RefreshLocalizedProperties();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"Language change failed: {ex}");
		}
	}

	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(RefreshLocalizedProperties);
	}

	private void RefreshLocalizedProperties()
	{
		RefreshTheme();
		OnPropertyChanged(nameof(AvailableLanguages));
		OnPropertyChanged(nameof(LanguageCode));
		OnPropertyChanged(nameof(MaxResultsText));
		OnPropertyChanged(nameof(TimeoutText));
		OnPropertyChanged(nameof(Version));
	}

	private void Reset()
	{
		_settings.ResetJourneyDefaults();

		OnPropertyChanged(nameof(MaxResults));
		OnPropertyChanged(nameof(MaxResultsText));
		OnPropertyChanged(nameof(DefaultArrival));
		OnPropertyChanged(nameof(ShowWalkingLegs));
		OnPropertyChanged(nameof(ExpandNotices));
		OnPropertyChanged(nameof(TimeoutSeconds));
		OnPropertyChanged(nameof(TimeoutText));
	}
}