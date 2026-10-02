using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public sealed partial class PlanViewModel : ObservableObject
{
	private static readonly TimeSpan RolloverGrace =
		TimeSpan.FromMinutes(30);

	private readonly PlaceStore _store;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	private DateTime _when;
	private bool _syncing;

	/// <summary>Whether the platform can follow journeys (shows the entry to the overview).</summary>
	public bool IsTrackingAvailable { get; }

	public PlanViewModel(
		PlaceStore store,
		AppSettings settings,
		IJourneyTracker tracker)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(tracker);

		IsTrackingAvailable = tracker.IsAvailable;

		_store = store;
		_settings = settings;
		_localization = LocalizationService.Current;

		_store.Changed += OnStoreChanged;
		_localization.PropertyChanged +=
			OnLocalizationChanged;

		PickFromCommand =
			new AsyncCommand(
				() => SafeAsync(
					() => PickAsync(true)));

		PickToCommand =
			new AsyncCommand(
				() => SafeAsync(
					() => PickAsync(false)));

		ToggleFromFavouriteCommand =
			new Command(
				() => Safe(
					() => ToggleFavourite(From)));

		ToggleToFavouriteCommand =
			new Command(
				() => Safe(
					() => ToggleFavourite(To)));

		SwapCommand =
			new Command(
				() => Safe(Swap));

		DepartCommand =
			new Command(
				() => IsArrival = false);

		ArriveCommand =
			new Command(
				() => IsArrival = true);

		NowCommand =
			new Command(
				() => Safe(SetNow));

		NudgeCommand =
			new Command<string>(
				minutes =>
					Safe(
						() => Nudge(minutes)));

		SearchCommand =
			new AsyncCommand(
				() => SafeAsync(SearchAsync),
				() => CanSearch);

		ClearRecentsCommand =
			new Command(
				() => Safe(
					_store.ClearRecents));

		SetNow();

		if (_settings.DefaultArrival)
		{
			IsArrival = true;
		}

		RefreshPlaces();
	}


	public Func<bool, Task>? OpenPlaceSearch { get; set; }

	public Func<JourneyQuery, Task>? OpenResults { get; set; }

	public Func<string, Task>? ShowError { get; set; }


	public AsyncCommand PickFromCommand { get; }

	public AsyncCommand PickToCommand { get; }

	public Command ToggleFromFavouriteCommand { get; }

	public Command ToggleToFavouriteCommand { get; }

	public Command SwapCommand { get; }

	public Command DepartCommand { get; }

	public Command ArriveCommand { get; }

	public Command NowCommand { get; }

	public Command<string> NudgeCommand { get; }

	public AsyncCommand SearchCommand { get; }

	public Command ClearRecentsCommand { get; }


	public ObservableCollection<Location> Recents { get; } = [];

	public ObservableCollection<Location> Favourites { get; } = [];


	public Location? From
	{
		get => field;

		set
		{
			if (SetProperty(
				ref field,
				value))
			{
				OnPropertyChanged(
					nameof(FromText));

				OnPropertyChanged(
					nameof(HasFrom));

				OnPropertyChanged(
					nameof(NoFrom));

				OnPropertyChanged(
					nameof(StarOpacityFrom));

				OnPropertyChanged(
					nameof(FromStar));

				OnRouteChanged();
			}
		}
	}


	public Location? To
	{
		get => field;

		set
		{
			if (SetProperty(
				ref field,
				value))
			{
				OnPropertyChanged(
					nameof(ToText));

				OnPropertyChanged(
					nameof(HasTo));

				OnPropertyChanged(
					nameof(NoTo));

				OnPropertyChanged(
					nameof(StarOpacityTo));

				OnPropertyChanged(
					nameof(ToStar));

				OnRouteChanged();
			}
		}
	}


	public string FromText =>
		From?.ToString()
		?? _localization
			.CurrentStrings
			.Plan
			.ChooseStart;


	public string ToText =>
		To?.ToString()
		?? _localization
			.CurrentStrings
			.Plan
			.ChooseDestination;


	public bool HasFrom =>
		From is not null;


	public bool NoFrom =>
		From is null;


	public bool NoTo =>
		To is null;


	public double StarOpacityFrom =>
		From is null
			? 0
			: 1;


	public double StarOpacityTo =>
		To is null
			? 0
			: 1;


	public bool HasTo =>
		To is not null;


	public string FromStar =>
		Star(From);


	public string ToStar =>
		Star(To);


	public DateTime Date
	{
		get => _when.Date;

		set
		{
			if (!_syncing)
			{
				SetWhen(
					value.Date + _when.TimeOfDay,
					fromTimeOfDay: false);
			}
		}
	}


	public TimeSpan Time
	{
		get => _when.TimeOfDay;

		set
		{
			if (!_syncing)
			{
				SetWhen(
					_when.Date
					+ TimeSpan.FromMinutes(
						Math.Floor(
							value.TotalMinutes)),
					fromTimeOfDay: true);
			}
		}
	}


	public DateTime MinDate
	{
		get => field;

		private set => SetProperty(
			ref field,
			value);
	} = Format.NowLocal().Date;


	public DateTime MaxDate =>
		MinDate.AddYears(1);


	public string WhenText =>
		IsNow
			? _localization
				.CurrentStrings
				.Plan
				.LeaveNow
			: $"{Format.DayLabel(_when)} \u00B7 " +
			  $"{_when.ToString(
				  "HH:mm",
				  CultureInfo.CurrentCulture)}";


	public string ModeText =>
		IsArrival
			? _localization
				.CurrentStrings
				.Plan
				.Arrival
			: _localization
				.CurrentStrings
				.Plan
				.Departure;


	public bool IsArrival
	{
		get => field;

		set
		{
			if (SetProperty(
				ref field,
				value))
			{
				if (value)
				{
					IsNow = false;
				}

				OnPropertyChanged(
					nameof(ModeText));

				OnPropertyChanged(
					nameof(WhenText));
			}
		}
	}


	public bool IsNow
	{
		get => field;

		private set
		{
			if (SetProperty(
				ref field,
				value))
			{
				OnPropertyChanged(
					nameof(IsNotNow));

				OnPropertyChanged(
					nameof(WhenText));
			}
		}
	}


	public bool IsNotNow =>
		!IsNow;


	public bool HasFavourites =>
		Favourites.Count > 0;


	public bool HasRecents =>
		Recents.Count > 0;


	public bool IsPlacesEmpty =>
		!HasFavourites
		&& !HasRecents;


	public bool CanSearch =>
		From is not null
		&& To is not null
		&& !SamePlace(
			From,
			To);


	public void Refresh()
	{
		Safe(
			() =>
			{
				DateTime now =
					Format.NowLocal();

				MinDate =
					now.Date;

				OnPropertyChanged(
					nameof(MaxDate));

				if (IsNow)
				{
					SetNow();
				}
				else if (_when < now)
				{
					Apply(now);
				}
			});
	}


	public void UsePlace(
		Location place)
	{
		ArgumentNullException.ThrowIfNull(
			place);

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
		if (From is null
			|| To is null)
		{
			throw new InvalidOperationException(
				_localization
					.CurrentStrings
					.Plan
					.StartAndDestinationRequired);
		}

		DateTime now =
			Format.NowLocal();

		DateTime target =
			IsNow
				? now
				: _when < now
					? now
					: _when;

		return new JourneyQuery
		{
			From = From,
			To = To,
			DateTime =
				IsNow
					? DateTimeOffset.Now
					: Format.ToOffset(target),
			SearchMode =
				IsArrival
					? JourneySearchMode.Arrival
					: JourneySearchMode.Departure,
			MaxResults =
				_settings.MaxResults,
			TimeoutSeconds =
				_settings.TimeoutSeconds
		};
	}


	private void SetWhen(
		DateTime candidate,
		bool fromTimeOfDay)
	{
		DateTime now =
			Format.NowLocal();

		if (candidate < now)
		{
			candidate =
				fromTimeOfDay
				&& candidate.Date == now.Date
				&& now - candidate > RolloverGrace
					? candidate.AddDays(1)
					: now;
		}

		IsNow = false;

		Apply(candidate);
	}


	private void Nudge(
		string? minutes)
	{
		if (!int.TryParse(
			minutes,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out int delta))
		{
			return;
		}

		DateTime basis =
			IsNow
				? Format.NowLocal()
				: _when;

		DateTime now =
			Format.NowLocal();

		DateTime next =
			basis.AddMinutes(delta);

		IsNow = false;

		Apply(
			next < now
				? now
				: next);
	}


	private void SetNow()
	{
		Apply(
			Format.NowLocal());

		IsArrival = false;
		IsNow = true;
	}


	private void Apply(
		DateTime value)
	{
		_syncing = true;

		try
		{
			_when =
				new DateTime(
					value.Year,
					value.Month,
					value.Day,
					value.Hour,
					value.Minute,
					0,
					DateTimeKind.Unspecified);

			OnPropertyChanged(
				nameof(Date));

			OnPropertyChanged(
				nameof(Time));

			OnPropertyChanged(
				nameof(WhenText));
		}
		finally
		{
			_syncing = false;
		}
	}


	private async Task PickAsync(
		bool isFrom)
	{
		if (OpenPlaceSearch is not null)
		{
			await OpenPlaceSearch(isFrom);
		}
	}


	private async Task SearchAsync()
	{
		if (!CanSearch
			|| OpenResults is null)
		{
			return;
		}

		JourneyQuery query =
			BuildQuery();

		_store.AddRecent(query.To);
		_store.AddRecent(query.From);

		await OpenResults(query);
	}


	private void Swap() =>
		(From, To) =
			(To, From);


	private void ToggleFavourite(
		Location? place)
	{
		if (place is not null)
		{
			_store.SetFavourite(
				place,
				!_store.IsFavourite(place));
		}
	}


	private string Star(
		Location? place)
	{
		try
		{
			return place is not null
				&& _store.IsFavourite(place)
					? "\u2605"
					: "\u2606";
		}
		catch
		{
			return "\u2606";
		}
	}


	private void OnRouteChanged()
	{
		SearchCommand
			.RaiseCanExecuteChanged();
	}


	private void OnStoreChanged(
		object? sender,
		EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			RefreshPlaces);


	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			RefreshLocalizedProperties);


	private void RefreshPlaces()
	{
		Safe(
			() =>
			{
				Replace(
					Recents,
					_store.Recents);

				Replace(
					Favourites,
					_store.Favourites);

				OnPropertyChanged(
					nameof(HasFavourites));

				OnPropertyChanged(
					nameof(HasRecents));

				OnPropertyChanged(
					nameof(IsPlacesEmpty));

				OnPropertyChanged(
					nameof(FromStar));

				OnPropertyChanged(
					nameof(ToStar));
			});
	}


	private void RefreshLocalizedProperties()
	{
		OnPropertyChanged(
			nameof(FromText));

		OnPropertyChanged(
			nameof(ToText));

		OnPropertyChanged(
			nameof(WhenText));

		OnPropertyChanged(
			nameof(ModeText));
	}


	private static void Replace(
		ObservableCollection<Location> target,
		IReadOnlyList<Location> source)
	{
		target.Clear();

		foreach (Location place in source)
		{
			target.Add(place);
		}
	}


	private static bool SamePlace(
		Location a,
		Location b) =>
		a.IsStation
			&& b.IsStation
			? a.Id == b.Id
			: a.Name == b.Name
				&& a.Place == b.Place;


	private void Safe(
		Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Report(ex);
		}
	}


	private async Task SafeAsync(
		Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Report(ex);
		}
	}


	private void Report(
		Exception ex)
	{
		System.Diagnostics.Debug.WriteLine(
			$"Plan error:\n{ex}");

		try
		{
			ShowError?.Invoke(
				ex.Message);
		}
		catch (Exception inner)
		{
			System.Diagnostics.Debug.WriteLine(
				$"Plan error reporting failed: " +
				$"{inner.Message}");
		}
	}
}