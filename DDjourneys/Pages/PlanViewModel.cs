using System.Collections.ObjectModel;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public sealed partial class PlanViewModel : ObservableObject
{
	private readonly PlaceStore _store;
	private bool _syncing;

	public PlanViewModel(PlaceStore store)
	{
		ArgumentNullException.ThrowIfNull(store);
		_store = store;
		_store.Changed += (_, _) => RefreshPlaces();

		PickFromCommand = new Command(async () => await PickAsync(isFrom: true));
		PickToCommand = new Command(async () => await PickAsync(isFrom: false));
		ToggleFromFavouriteCommand = new Command(() => ToggleFavourite(From));
		ToggleToFavouriteCommand = new Command(() => ToggleFavourite(To));
		SwapCommand = new Command(Swap);
		DepartCommand = new Command(() => IsArrival = false);
		ArriveCommand = new Command(() => IsArrival = true);
		NowCommand = new Command(SetNow);
		SearchCommand = new Command(async () => await SearchAsync(), () => CanSearch);
		ClearRecentsCommand = new Command(_store.ClearRecents);

		SetNow();
		RefreshPlaces();
	}

	public Func<bool, Task>? OpenPlaceSearch { get; set; }
	public Func<JourneyQuery, Task>? OpenResults { get; set; }

	public Command PickFromCommand { get; }
	public Command PickToCommand { get; }
	public Command ToggleFromFavouriteCommand { get; }
	public Command ToggleToFavouriteCommand { get; }
	public Command SwapCommand { get; }
	public Command DepartCommand { get; }
	public Command ArriveCommand { get; }
	public Command NowCommand { get; }
	public Command SearchCommand { get; }
	public Command ClearRecentsCommand { get; }

	public ObservableCollection<Location> Recents { get; } = [];
	public ObservableCollection<Location> Favourites { get; } = [];

	public Location? From
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(FromText));
				OnPropertyChanged(nameof(HasFrom));
				OnPropertyChanged(nameof(FromStar));
				OnRouteChanged();
			}
		}
	}

	public Location? To
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(ToText));
				OnPropertyChanged(nameof(HasTo));
				OnPropertyChanged(nameof(ToStar));
				OnRouteChanged();
			}
		}
	}

	public string FromText => From?.ToString() ?? "Choose start";
	public string ToText => To?.ToString() ?? "Choose destination";
	public bool HasFrom => From is not null;
	public bool HasTo => To is not null;
	public string FromStar => Star(From);
	public string ToStar => Star(To);

	/// <summary>The chosen day. Picking one means "not now".</summary>
	public DateTime Date
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value.Date) && !_syncing)
			{
				IsNow = false;
			}
		}
	}

	/// <summary>The chosen time of day. Picking one means "not now".</summary>
	public TimeSpan Time
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value) && !_syncing)
			{
				IsNow = false;
			}
		}
	}

	public DateTime MinDate => DateTime.Today;

	public bool IsArrival
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value) && value)
			{
				IsNow = false; // "arrive by now" makes no sense
			}
		}
	}

	/// <summary>True until the user picks a date or time. Search then uses the moment of searching.</summary>
	public bool IsNow
	{
		get => field;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(IsNotNow));
			}
		}
	}

	public bool IsNotNow => !IsNow;

	public bool HasFavourites => Favourites.Count > 0;
	public bool HasRecents => Recents.Count > 0;
	public bool IsPlacesEmpty => !HasFavourites && !HasRecents;

	public bool CanSearch =>
		From is not null && To is not null && !SamePlace(From, To);

	/// <summary>Fills the empty start first, otherwise replaces the destination.</summary>
	public void UsePlace(Location place)
	{
		ArgumentNullException.ThrowIfNull(place);

		if (From is null)
		{
			From = place;
		}
		else
		{
			To = place;
		}
	}

	public JourneyQuery BuildQuery()
	{
		if (From is null || To is null)
		{
			throw new InvalidOperationException("Start and destination are required.");
		}

		return new JourneyQuery
		{
			From = From,
			To = To,
			DateTime = IsNow ? DateTimeOffset.Now : ChosenMoment(),
			SearchMode = IsArrival ? JourneySearchMode.Arrival : JourneySearchMode.Departure
		};
	}

	private async Task PickAsync(bool isFrom)
	{
		if (OpenPlaceSearch is null)
		{
			return;
		}

		await OpenPlaceSearch(isFrom);
	}

	private async Task SearchAsync()
	{
		if (!CanSearch || OpenResults is null)
		{
			return;
		}

		JourneyQuery query = BuildQuery();

		_store.AddRecent(query.To);
		_store.AddRecent(query.From);

		await OpenResults(query);
	}

	private void Swap() => (From, To) = (To, From);

	private void SetNow()
	{
		_syncing = true;
		DateTime now = DateTime.Now;
		Date = now;
		Time = TimeSpan.FromMinutes(Math.Floor(now.TimeOfDay.TotalMinutes));
		_syncing = false;

		IsArrival = false;
		IsNow = true;
	}

	private void ToggleFavourite(Location? place)
	{
		if (place is not null)
		{
			_store.SetFavourite(place, !_store.IsFavourite(place));
		}
	}

	private DateTimeOffset ChosenMoment()
	{
		// Uses the device time zone; assumes the user is where the journey starts.
		var local = DateTime.SpecifyKind(Date.Date + Time, DateTimeKind.Unspecified);
		return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
	}

	private string Star(Location? place) =>
		place is not null && _store.IsFavourite(place) ? "\u2605" : "\u2606";

	private void OnRouteChanged() => SearchCommand.ChangeCanExecute();

	private void RefreshPlaces()
	{
		Replace(Recents, _store.Recents);
		Replace(Favourites, _store.Favourites);

		OnPropertyChanged(nameof(HasFavourites));
		OnPropertyChanged(nameof(HasRecents));
		OnPropertyChanged(nameof(IsPlacesEmpty));
		OnPropertyChanged(nameof(FromStar));
		OnPropertyChanged(nameof(ToStar));
	}

	private static void Replace(ObservableCollection<Location> target, IReadOnlyList<Location> source)
	{
		target.Clear();

		foreach (Location place in source)
		{
			target.Add(place);
		}
	}

	private static bool SamePlace(Location a, Location b) =>
		a.IsStation && b.IsStation
			? a.Id == b.Id
			: a.Name == b.Name && a.Place == b.Place;
}
