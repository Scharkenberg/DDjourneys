using DDjourneys.Core.Models;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>What the app opens on: the start screen (the planner or, on the phone, the map tab), the input mode with its start filled in and the destination search ready.</summary>
public sealed partial class StartSettingsViewModel : DisposableViewModel
{
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	public StartSettingsViewModel(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
		_localization = LocalizationService.Current;

		SelectPlannerCommand = new Command(() => SelectScreen(StartScreen.Planner));
		SelectMapCommand = new Command(() => SelectScreen(StartScreen.Map));
		SelectLocationCommand = new Command(() => Select(StartFromKind.Location));
		SelectPlaceCommand = new AsyncCommand(SelectPlaceAsync);
		PickPlaceCommand = new AsyncCommand(PickPlaceAsync);
	}

	public Command SelectPlannerCommand { get; }

	public Command SelectMapCommand { get; }

	public Command SelectLocationCommand { get; }

	public AsyncCommand SelectPlaceCommand { get; }

	public AsyncCommand PickPlaceCommand { get; }

	/// <summary>The app has tabs (the phone), so the start screen can be chosen; everywhere else the planner always opens.</summary>
	public bool HasTabs =>
		TabRoots.Enabled;

	public bool StartOnPlanner =>
		_settings.StartScreen != StartScreen.Map;

	public bool StartOnMap =>
		!StartOnPlanner;

	public bool InputMode
	{
		get => _settings.StartInput;
		set
		{
			_settings.StartInput = value;

			OnPropertyChanged();
		}
	}

	public bool UseLocation =>
		_settings.StartFrom == StartFromKind.Location;

	public bool UsePlace =>
		!UseLocation;

	public string PlaceName =>
		_settings.StartFromPlace?.Name
		?? _localization.CurrentStrings.Settings.StartPlaceNone;

	public string? PlaceDetail =>
		_settings.StartFromPlace?.Place;

	public bool HasPlaceDetail =>
		!string.IsNullOrWhiteSpace(PlaceDetail);

	public bool HasPlace =>
		_settings.StartFromPlace is not null;

	/// <summary>The chosen place comes back from the place search.</summary>
	public void SetPlace(Location place)
	{
		ArgumentNullException.ThrowIfNull(place);

		_settings.StartFromPlace = place;
		_settings.StartFrom = StartFromKind.Place;

		Refresh();
	}

	public void Refresh()
	{
		OnPropertyChanged(nameof(StartOnPlanner));
		OnPropertyChanged(nameof(StartOnMap));
		OnPropertyChanged(nameof(InputMode));
		OnPropertyChanged(nameof(UseLocation));
		OnPropertyChanged(nameof(UsePlace));
		OnPropertyChanged(nameof(PlaceName));
		OnPropertyChanged(nameof(PlaceDetail));
		OnPropertyChanged(nameof(HasPlaceDetail));
		OnPropertyChanged(nameof(HasPlace));
	}

	private void SelectScreen(StartScreen screen)
	{
		_settings.StartScreen = screen;

		Refresh();
	}

	private void Select(StartFromKind kind)
	{
		_settings.StartFrom = kind;

		Refresh();
	}

	/// <summary>"A place" with none chosen yet goes straight to the search.</summary>
	private async Task SelectPlaceAsync()
	{
		Select(StartFromKind.Place);

		if (!HasPlace)
		{
			await PickPlaceAsync();
		}
	}

	private static async Task PickPlaceAsync()
	{
		try
		{
			await Shell.Current.GoToAsync(
				Routes.PlaceSearch,
				new ShellNavigationQueryParameters
				{
					[Routes.TargetIsFrom] = true,
					[Routes.Target] = Routes.TargetStart
				});
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Choosing the start place failed: {ex.Message}");
		}
	}
}
