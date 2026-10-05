using DDjourneys.Core.Models;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>"Start in input mode": whether the app opens with the start filled in and the destination search ready, and what the start is.</summary>
public sealed class StartSettingsViewModel : DisposableViewModel
{
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	public StartSettingsViewModel(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
		_localization = LocalizationService.Current;

		SelectLocationCommand = new Command(() => Select(StartFromKind.Location));
		SelectPlaceCommand = new AsyncCommand(SelectPlaceAsync);
		PickPlaceCommand = new AsyncCommand(PickPlaceAsync);
	}

	public Command SelectLocationCommand { get; }

	public AsyncCommand SelectPlaceCommand { get; }

	public AsyncCommand PickPlaceCommand { get; }

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
		OnPropertyChanged(nameof(InputMode));
		OnPropertyChanged(nameof(UseLocation));
		OnPropertyChanged(nameof(UsePlace));
		OnPropertyChanged(nameof(PlaceName));
		OnPropertyChanged(nameof(PlaceDetail));
		OnPropertyChanged(nameof(HasPlaceDetail));
		OnPropertyChanged(nameof(HasPlace));
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
