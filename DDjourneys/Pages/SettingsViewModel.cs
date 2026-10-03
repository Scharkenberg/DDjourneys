using DDjourneys.Core.Providers;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Thin, two-way view over <see cref="AppSettings"/>. Persisting is AppSettings' job.</summary>
public sealed class SettingsViewModel : DisposableViewModel
{
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	private readonly ProviderRegistry _providers;

	public SettingsViewModel(AppSettings settings, ProviderRegistry providers)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(providers);

		_settings = settings;
		_providers = providers;
		_localization = LocalizationService.Current;

		ListenToLocalization(_localization, OnLocalizationChanged);

		OpenAppearanceCommand = new AsyncCommand(OpenAppearanceAsync);
		SelectLanguageCommand = new Command<string>(SelectLanguage);
		ResetCommand = new Command(Reset);
		OpenRoutingCommand = new AsyncCommand(OpenRoutingAsync);
		OpenProvidersCommand = new AsyncCommand(OpenProvidersAsync);
	}

	public AsyncCommand OpenAppearanceCommand { get; }
	public Command<string> SelectLanguageCommand { get; }
	public Command ResetCommand { get; }
	public AsyncCommand OpenRoutingCommand { get; }
	public AsyncCommand OpenProvidersCommand { get; }

	/// <summary>Name of the selected provider, shown on the entry row.</summary>
	public string ProviderText =>
		_providers.Selected is { } provider
			? $"{provider.Name} \u00b7 {provider.FullName}"
			: string.Empty;

	public void RefreshProvider() =>
		OnPropertyChanged(nameof(ProviderText));

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

	/// <summary>Lead time for newly followed journeys; existing ones keep their own (changeable per journey).</summary>
	public double LeadMinutes
	{
		get => _settings.DefaultLeadMinutes;
		set
		{
			int minutes = (int)Math.Round(value);

			if (minutes != _settings.DefaultLeadMinutes)
			{
				_settings.DefaultLeadMinutes = minutes;
			}

			OnPropertyChanged();
			OnPropertyChanged(nameof(LeadText));
		}
	}

	public string LeadText =>
		string.Format(
			_localization.CurrentStrings.Settings.LeadMinutesDescription,
			_settings.DefaultLeadMinutes);

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

	// ----- Journey display -----

	public bool ShowOccupancy
	{
		get => _settings.ShowOccupancy;
		set
		{
			_settings.ShowOccupancy = value;
			OnPropertyChanged();
		}
	}

	public bool ShowPlatforms
	{
		get => _settings.ShowPlatforms;
		set
		{
			_settings.ShowPlatforms = value;
			OnPropertyChanged();
		}
	}

	public bool ExpandStops
	{
		get => _settings.ExpandStops;
		set
		{
			_settings.ExpandStops = value;
			OnPropertyChanged();
		}
	}

	public bool ExpertView
	{
		get => _settings.ExpertView;
		set
		{
			_settings.ExpertView = value;
			OnPropertyChanged();
		}
	}

	// ----- Place search -----

	public double SearchDelayMs
	{
		get => _settings.SearchDelayMs;
		set
		{
			int stepped = (int)Math.Round(value / 100.0) * 100;

			if (stepped != _settings.SearchDelayMs)
			{
				_settings.SearchDelayMs = stepped;
			}

			OnPropertyChanged();
			OnPropertyChanged(nameof(SearchDelayText));
		}
	}

	public string SearchDelayText =>
		string.Format(
			_localization.CurrentStrings.Settings.SearchDelayDescription,
			_settings.SearchDelayMs);

	public double MinQueryLength
	{
		get => _settings.MinQueryLength;
		set
		{
			int rounded = (int)Math.Round(value);

			if (rounded != _settings.MinQueryLength)
			{
				_settings.MinQueryLength = rounded;
			}

			OnPropertyChanged();
			OnPropertyChanged(nameof(MinQueryLengthText));
		}
	}

	public string MinQueryLengthText =>
		string.Format(
			_localization.CurrentStrings.Settings.MinQueryLengthDescription,
			_settings.MinQueryLength);

	public double MinQueryLengthFloor => AppSettings.MinQueryLengthFloor;

	public double MinQueryLengthCeiling => AppSettings.MinQueryLengthCeiling;

	public double MinSearchDelay => AppSettings.MinSearchDelayMs;

	public double MaxSearchDelay => AppSettings.MaxSearchDelayMs;

	private static Task OpenProvidersAsync() =>
		Shell.Current.GoToAsync(Routes.Providers);

	private static Task OpenRoutingAsync() =>
		Shell.Current.GoToAsync(Routes.Routing);

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

	// ----- Appearance: details live on their own page -----

	/// <summary>Current choice in one line, for example "System · Default · Open Sans".</summary>
	public string AppearanceSummary => AppearanceViewModel.Summary(_localization);

	/// <summary>Called when the page (re)appears, e.g. after returning from the appearance page.</summary>
	public void RefreshAppearance() => OnPropertyChanged(nameof(AppearanceSummary));

	private async Task OpenAppearanceAsync()
	{
		try
		{
			await Shell.Current.GoToAsync(Routes.Appearance);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Opening appearance failed: {ex}");
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
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					RefreshLocalizedProperties();
				}
			});
	}

	private void RefreshLocalizedProperties()
	{
		RefreshAppearance();
		OnPropertyChanged(nameof(AvailableLanguages));
		OnPropertyChanged(nameof(LanguageCode));
		OnPropertyChanged(nameof(MaxResultsText));
		OnPropertyChanged(nameof(TimeoutText));
		OnPropertyChanged(nameof(SearchDelayText));
		OnPropertyChanged(nameof(MinQueryLengthText));
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
		OnPropertyChanged(nameof(LeadMinutes));
		OnPropertyChanged(nameof(LeadText));
		OnPropertyChanged(nameof(ShowOccupancy));
		OnPropertyChanged(nameof(ShowPlatforms));
		OnPropertyChanged(nameof(ExpandStops));
		OnPropertyChanged(nameof(ExpertView));
		OnPropertyChanged(nameof(SearchDelayMs));
		OnPropertyChanged(nameof(SearchDelayText));
		OnPropertyChanged(nameof(MinQueryLength));
		OnPropertyChanged(nameof(MinQueryLengthText));
	}
}