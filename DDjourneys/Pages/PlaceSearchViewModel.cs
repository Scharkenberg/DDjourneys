using System.Collections.ObjectModel;
using System.Windows.Input;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Core.Storage;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public sealed partial class PlaceSearchViewModel : ObservableObject, IQueryAttributable
{
	private readonly LocationService _locationService;
	private readonly PlaceStore _placeStore;

	private string _query = string.Empty;
	private bool _targetIsFrom;
	private bool _isSearching;

	public PlaceSearchViewModel(
		LocationService locationService,
		PlaceStore placeStore)
	{
		_locationService = locationService;
		_placeStore = placeStore;

		SearchCommand = new Command(async () => await SearchAsync());
		SelectPlaceCommand = new Command<Location>(async place => await SelectPlaceAsync(place));

		LoadRecents();
	}

	public ObservableCollection<Location> Results { get; } = [];

	public ObservableCollection<Location> Recents { get; } = [];

	public string Query
	{
		get => _query;
		set
		{
			if (SetProperty(ref _query, value))
			{
				_ = SearchAsync();
			}
		}
	}

	public bool IsSearching
	{
		get => _isSearching;
		set => SetProperty(ref _isSearching, value);
	}

	public ICommand SearchCommand { get; }

	public ICommand SelectPlaceCommand { get; }

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.TargetIsFrom, out object? targetIsFrom))
		{
			if (targetIsFrom is not bool isFrom)
			{
				throw new InvalidOperationException("Place search received invalid navigation data.");
			}

			_targetIsFrom = isFrom;
		}
	}

	private Location? _selectedPlace;

	public Location? SelectedPlace
	{
		get => _selectedPlace;
		set => SetProperty(ref _selectedPlace, value);
	}

	private void LoadRecents()
	{
		Recents.Clear();

		foreach (var place in _placeStore.Recents)
		{
			Recents.Add(place);
		}
	}

	private async Task SearchAsync()
	{
		if (string.IsNullOrWhiteSpace(Query))
		{
			Results.Clear();
			return;
		}

		IsSearching = true;

		try
		{
			var locations = await _locationService.SearchAsync(Query);

			Results.Clear();

			foreach (var location in locations)
			{
				Results.Add(location);
			}
		}
		finally
		{
			IsSearching = false;
		}
	}

	private async Task SelectPlaceAsync(Location? place)
	{
		if (place is null)
		{
			return;
		}

		_placeStore.AddRecent(place);

		await Shell.Current.GoToAsync("..", new Dictionary<string, object>
		{
			[Routes.SelectedPlace] = place,
			[Routes.TargetIsFrom] = _targetIsFrom
		});
	}
}
