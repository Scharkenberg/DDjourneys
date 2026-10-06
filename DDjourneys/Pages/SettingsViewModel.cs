using CommunityToolkit.Maui.Alerts;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers;
using DDjourneys.Localization;
using DDjourneys.Support;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DDjourneys.Pages;

/// <summary>Thin, two-way view over <see cref="AppSettings"/>. Persisting is AppSettings' job.</summary>
public sealed partial class SettingsViewModel : DisposableViewModel
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
		OpenStartCommand = new AsyncCommand(OpenStartAsync);
		OpenProvidersCommand = new AsyncCommand(OpenProvidersAsync);
		ShareLogCommand = new AsyncCommand(ShareLogAsync);
		ClearLogCommand = new Command(ClearLog);
		OpenAboutCommand = new AsyncCommand(OpenAboutAsync);
		CopyInterfacesCommand = new AsyncCommand(CopyInterfacesAsync);
		SaveMapKeyCommand = new Command(SaveMapKey, () => MapKeyChanged);
		SelectMapEngineCommand = new Command<string>(SelectMapEngine);

		_mapKeyDraft = _settings.MapApiKey;

		Subscribe(
			() => MapAvailability.Changed += OnMapAvailabilityChanged,
			() => MapAvailability.Changed -= OnMapAvailabilityChanged);
	}

	private string _mapKeyDraft;

	/// <summary>What is typed in the key field; it is used when "Use key" is pressed (not on every keystroke).</summary>
	public string MapKeyDraft
	{
		get => _mapKeyDraft;
		set
		{
			if (SetProperty(ref _mapKeyDraft, value ?? string.Empty))
			{
				OnPropertyChanged(nameof(MapKeyChanged));
				SaveMapKeyCommand.ChangeCanExecute();
			}
		}
	}

	public bool MapKeyChanged =>
		!string.Equals(_mapKeyDraft.Trim(), _settings.MapApiKey, StringComparison.Ordinal);

	public Command SaveMapKeyCommand { get; }

	public Command<string> SelectMapEngineCommand { get; }

	public bool MapEngineIsCarto =>
		_settings.MapEngine == MapEngine.Carto;

	public bool MapEngineIsLeaflet =>
		_settings.MapEngine == MapEngine.Leaflet;

	private void SelectMapEngine(string? id)
	{
		_settings.MapEngine =
			string.Equals(id, MapAvailability.LeafletId, StringComparison.Ordinal)
				? MapEngine.Leaflet
				: MapEngine.Carto;

		OnPropertyChanged(nameof(MapEngineIsCarto));
		OnPropertyChanged(nameof(MapEngineIsLeaflet));
	}

	public string MapKeyStatus
	{
		get
		{
			SettingsStrings strings = _localization.CurrentStrings.Settings;

			return !MapAvailability.HasKey
				? strings.MapKeyStatusMissing
				: MapAvailability.IsRejected
					? strings.MapKeyStatusInvalid
					: strings.MapKeyStatusSet;
		}
	}

	private void SaveMapKey()
	{
		_settings.MapApiKey = _mapKeyDraft.Trim();

		OnPropertyChanged(nameof(MapKeyChanged));
		OnPropertyChanged(nameof(MapKeyStatus));
		SaveMapKeyCommand.ChangeCanExecute();
	}

	private void OnMapAvailabilityChanged(object? sender, EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					OnPropertyChanged(nameof(MapKeyStatus));
					OnPropertyChanged(nameof(MapKeyChanged));
					OnPropertyChanged(nameof(MapEngineIsCarto));
					OnPropertyChanged(nameof(MapEngineIsLeaflet));
				}
			});

	public AsyncCommand OpenAppearanceCommand { get; }
	public Command<string> SelectLanguageCommand { get; }
	public Command ResetCommand { get; }
	public AsyncCommand OpenRoutingCommand { get; }
	public AsyncCommand OpenStartCommand { get; }
	public AsyncCommand OpenProvidersCommand { get; }
	public AsyncCommand ShareLogCommand { get; }
	public Command ClearLogCommand { get; }
	public AsyncCommand OpenAboutCommand { get; }
	public AsyncCommand CopyInterfacesCommand { get; }

	/// <summary>Build and the version of every interface, as the log file starts with it (see <see cref="AppInterfaces"/>).</summary>
	public string InterfaceReport => field ??= AppInterfaces.Report();

	/// <summary>Name of the selected provider, shown on the entry row.</summary>
	public string ProviderText =>
		_providers.Selected is { } provider
			? provider.IsExperimental
				? $"{provider.Name} \u00b7 {provider.FullName} \u00b7 {_localization.CurrentStrings.Provider.Experimental}"
				: $"{provider.Name} \u00b7 {provider.FullName}"
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

	public const double MinResults = AppSettings.MinResults;

	public const double MaxResultsLimit = AppSettings.MaxResultsLimit;

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

	// ----- Places -----

	public bool SearchAddresses
	{
		get => _settings.SearchAddresses;
		set
		{
			_settings.SearchAddresses = value;
			OnPropertyChanged();
		}
	}

	public bool SearchPois
	{
		get => _settings.SearchPois;
		set
		{
			_settings.SearchPois = value;
			OnPropertyChanged();
		}
	}

	public bool ExactPosition
	{
		get => _settings.ExactPosition;
		set
		{
			_settings.ExactPosition = value;
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

	// ----- Developer options -----

	public bool DeveloperOptions
	{
		get => _settings.DeveloperOptions;
		set
		{
			_settings.DeveloperOptions = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(ExpertView));
			OnPropertyChanged(nameof(LogToFile));
			OnPropertyChanged(nameof(HasLog));
		}
	}

	public bool LogToFile
	{
		get => _settings.LogToFile;
		set
		{
			_settings.LogToFile = value;
			OnPropertyChanged();
			RefreshLog();
		}
	}

	/// <summary>There is a log (or one is being written) to share or delete.</summary>
	public bool HasLog => LogToFile || DiagnosticLog.Exists;

	/// <summary>Where the log file is, how big it is, and why writing failed if it did.</summary>
	public string LogInfo { get; private set; } = DescribeLog();

	private static string DescribeLog()
	{
		string path = DiagnosticLog.FilePath ?? "-";

		string size =
			DiagnosticLog.Exists && DiagnosticLog.FilePath is { } file
				? $" ({new FileInfo(file).Length / 1024.0:0.#} KB)"
				: string.Empty;

		return DiagnosticLog.LastError is { } error
			? $"{path}{size}\n{error}"
			: $"{path}{size}";
	}

	/// <summary>Called when the page appears: the file may have grown or appeared since.</summary>
	public void RefreshLog()
	{
		LogInfo = DescribeLog();
		OnPropertyChanged(nameof(HasLog));
		OnPropertyChanged(nameof(LogInfo));
	}

	private async Task ShareLogAsync()
	{
		if (!DiagnosticLog.Exists || DiagnosticLog.FilePath is not { } path)
		{
			RefreshLog();

			return;
		}

		await Share.Default.RequestAsync(
			new ShareFileRequest(
				_localization.CurrentStrings.Settings.LogToFile,
				new ReadOnlyFile(path)));
	}

	private void ClearLog()
	{
		DiagnosticLog.Delete();
		RefreshLog();
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

	public const double MinQueryLengthFloor = AppSettings.MinQueryLengthFloor;

	public const double MinQueryLengthCeiling = AppSettings.MinQueryLengthCeiling;

	public const double MinSearchDelay = AppSettings.MinSearchDelayMs;

	public const double MaxSearchDelay = AppSettings.MaxSearchDelayMs;

	private static Task OpenAboutAsync() =>
		Shell.Current.GoToAsync(Routes.About);

	private async Task CopyInterfacesAsync()
	{
		await Clipboard.Default.SetTextAsync(InterfaceReport);

		await Toast.Make(_localization.CurrentStrings.Settings.InterfacesCopied).Show();
	}

	private static Task OpenProvidersAsync() =>
		Shell.Current.GoToAsync(Routes.Providers);

	private static Task OpenRoutingAsync() =>
		Shell.Current.GoToAsync(Routes.Routing);

	private static Task OpenStartAsync() =>
		Shell.Current.GoToAsync(Routes.StartSettings);

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
			DiagnosticLog.Write($"Opening appearance failed: {ex}");
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
			DiagnosticLog.Write(
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
		OnPropertyChanged(nameof(SearchAddresses));
		OnPropertyChanged(nameof(SearchPois));
		OnPropertyChanged(nameof(ExactPosition));
		OnPropertyChanged(nameof(ShowPlatforms));
		OnPropertyChanged(nameof(ExpandStops));
		OnPropertyChanged(nameof(SearchDelayMs));
		OnPropertyChanged(nameof(SearchDelayText));
		OnPropertyChanged(nameof(MinQueryLength));
		OnPropertyChanged(nameof(MinQueryLengthText));
	}
}