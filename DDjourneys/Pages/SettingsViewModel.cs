using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Thin, two-way view over <see cref="AppSettings"/>. Persisting is AppSettings' job.</summary>
public sealed class SettingsViewModel : ObservableObject
{
	private readonly AppSettings _settings;

	public SettingsViewModel(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		_settings = settings;

		SelectThemeCommand = new Command<ThemeChoice>(async choice => await SelectThemeAsync(choice));
		ResetCommand = new Command(Reset);
	}

	public Command<ThemeChoice> SelectThemeCommand { get; }
	public Command ResetCommand { get; }

	// ----- Appearance -----

	public bool IsSystem => _settings.Theme == ThemeChoice.System;
	public bool IsLight => _settings.Theme == ThemeChoice.Light;
	public bool IsDark => _settings.Theme == ThemeChoice.Dark;
	public bool IsAmoled => _settings.Theme == ThemeChoice.Amoled;

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
			_settings.MaxResults = (int)Math.Round(value);
			OnPropertyChanged();
			OnPropertyChanged(nameof(MaxResultsText));
		}
	}

	public string MaxResultsText => $"{_settings.MaxResults} journeys per search";

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
				return $"DDjourneys {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
			}
			catch (Exception)
			{
				return "DDjourneys";
			}
		}
	}

	private async Task SelectThemeAsync(ThemeChoice choice)
	{
		try
		{
			await Theme.SetAsync(choice);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Theme change failed: {ex}");
		}
		finally
		{
			OnPropertyChanged(nameof(IsSystem));
			OnPropertyChanged(nameof(IsLight));
			OnPropertyChanged(nameof(IsDark));
			OnPropertyChanged(nameof(IsAmoled));
		}
	}

	private void Reset()
	{
		_settings.ResetJourneyDefaults();
		OnPropertyChanged(nameof(MaxResults));
		OnPropertyChanged(nameof(MaxResultsText));
		OnPropertyChanged(nameof(DefaultArrival));
		OnPropertyChanged(nameof(ShowWalkingLegs));
		OnPropertyChanged(nameof(ExpandNotices));
	}
}
