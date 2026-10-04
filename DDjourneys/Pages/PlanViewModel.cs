using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Contract;
using DDjourneys.Controls;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Tracking;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;
using SavedLocation = DDjourneys.Core.Models.SavedLocation;
using SavedRoute = DDjourneys.Core.Models.SavedRoute;

namespace DDjourneys.Pages;

/// <summary>A connection the passenger searched before, as the planner lists it.</summary>
public sealed record RouteRow(
	RoutePair Route)
{
	public string FromName =>
		Route.From.Name;

	public string? FromPlace =>
		StopLabel.PlaceFor(
			Route.From.Name,
			Route.From.Place);

	public string ToName =>
		Route.To.Name;

	public string? ToPlace =>
		StopLabel.PlaceFor(
			Route.To.Name,
			Route.To.Place);

	public string Description =>
		$"{StopLabel.Compose(Route.From)} \u2192 {StopLabel.Compose(Route.To)}";
}


public sealed partial class PlanViewModel : DisposableViewModel
{
	/// <summary>How many searched connections the planner shows before "show all".</summary>
	private const int CollapsedRoutes = 5;

	private static readonly TimeSpan RolloverGrace =
		TimeSpan.FromMinutes(30);

	private readonly PlaceStore _store;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;
	private readonly ILocationService _locationService;

	private DateTime _when;
	private bool _syncing;
	private bool _isGettingLocation;

	private readonly ProviderRegistry _providers;
	private readonly Lazy<IJourneyTracker> _tracker;
	private string _providerId;

	/// <summary>Whether the platform and the selected provider can follow journeys (shows the entry to the overview).</summary>
	public bool IsTrackingAvailable =>
		_providers.Supports(ProviderCapabilities.Tracking)
		&& (!_tracker.IsValueCreated || _tracker.Value.IsAvailable);

	public PlanViewModel(
		PlaceStore store,
		AppSettings settings,
		Lazy<IJourneyTracker> tracker,
		ProviderRegistry providers,
		ILocationService locationService)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(tracker);
		ArgumentNullException.ThrowIfNull(providers);
		ArgumentNullException.ThrowIfNull(locationService);

		// Resolved on first use: building the tracking graph must not delay the first frame.
		_tracker = tracker;
		_providers = providers;
		_providerId = providers.SelectedId;

		_store = store;
		_settings = settings;
		_localization = LocalizationService.Current;
		_locationService = locationService;

		Subscribe(
			() => _store.Changed += OnStoreChanged,
			() => _store.Changed -= OnStoreChanged);

		ListenToLocalization(
			_localization,
			OnLocalizationChanged);

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

		ClearRoutesCommand =
			new Command(
				() => Safe(
					_store.ClearRecentRoutes));

		UseGpsFromCommand =
			new AsyncCommand(
				() => SafeAsync(
					() => SetLocationFromGpsAsync(true)));

		UseGpsToCommand =
			new AsyncCommand(
				() => SafeAsync(
					() => SetLocationFromGpsAsync(false)));

		TakeMeHomeCommand =
			new AsyncCommand(
				() => SafeAsync(TakeMeHomeAsync));

		LoadSavedRouteCommand =
			new AsyncCommand<SavedRoute>(
				route => SafeAsync(
					() => LoadSavedRouteAsync(route)));

		SaveCurrentRouteCommand =
			new AsyncCommand(
				() => SafeAsync(SaveCurrentRouteAsync));

		SetHomeLocationCommand =
			new AsyncCommand(
				() => SafeAsync(SetHomeLocationAsync));

		ClearHomeCommand =
			new Command(
				() => Safe(
					() => _store.ClearHome()));

		ClearSavedRoutesCommand =
			new Command(
				() => Safe(
					() => _store.SavedRoutes.ToList().ForEach(r => _store.RemoveSavedRoute(r))));

		ToggleRoutesCommand =
			new Command(
				() => ShowAllRoutes = !ShowAllRoutes);

		UseRouteCommand =
			new AsyncCommand<RouteRow>(
				row => SafeAsync(
					() => UseRouteAsync(row)));

		ForgetRouteCommand =
			new Command<RouteRow>(
				row => Safe(
					() =>
					{
						if (row is not null)
						{
							_store.RemoveRecentRoute(row.Route);
						}
					}));

		SetNow();

		if (_settings.DefaultArrival)
		{
			IsArrival = true;
		}

		IsGettingLocation = false;

		RefreshPlaces();
		RefreshSavedRoutes();
	}


	public Func<bool, Task>? OpenPlaceSearch { get; set; }

	public Func<JourneyQuery, Task>? OpenResults { get; set; }

	public Func<string, Task>? ShowError { get; set; }

	public Func<string, Task<string>>? ShowRouteNameDialog { get; set; }

	public Func<string, Task<string>>? ShowHomeLocationNameDialog { get; set; }

	public Func<Task<bool>>? CheckLocationPermission { get; set; }

	public Func<Task>? RequestLocationPermission { get; set; }


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

	public Command ClearRoutesCommand { get; }

	public Command ToggleRoutesCommand { get; }

	public AsyncCommand UseGpsFromCommand { get; }

	public AsyncCommand UseGpsToCommand { get; }

	public AsyncCommand TakeMeHomeCommand { get; }

	public AsyncCommand<SavedRoute> LoadSavedRouteCommand { get; }

	public AsyncCommand SaveCurrentRouteCommand { get; }

	public AsyncCommand SetHomeLocationCommand { get; }

	public Command ClearHomeCommand { get; }

	public Command ClearSavedRoutesCommand { get; }

	/// <summary>Takes a remembered connection and searches it again straight away.</summary>
	public AsyncCommand<RouteRow> UseRouteCommand { get; }

	public Command<RouteRow> ForgetRouteCommand { get; }

	public Command ClearRecentsCommand { get; }


	public ObservableCollection<Location> Recents { get; } = [];

	public ObservableCollection<Location> Favourites { get; } = [];

	public ObservableCollection<SavedRoute> SavedRoutes { get; } = [];


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
					nameof(FromName));

				OnPropertyChanged(
					nameof(FromPlace));

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
					nameof(ToName));

				OnPropertyChanged(
					nameof(ToPlace));

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


	/// <summary>Name line of the start; the placeholder when nothing is chosen yet.</summary>
	public string FromName =>
		From?.Name
		?? _localization
			.CurrentStrings
			.Plan
			.ChooseStart;


	/// <summary>City of the start, shown under the name.</summary>
	public string? FromPlace =>
		From is null
			? null
			: StopLabel.PlaceFor(
				From.Name,
				From.Place);


	public string ToName =>
		To?.Name
		?? _localization
			.CurrentStrings
			.Plan
			.ChooseDestination;


	public string? ToPlace =>
		To is null
			? null
			: StopLabel.PlaceFor(
				To.Name,
				To.Place);


	/// <summary>The connections searched before, most recent first.</summary>
	public ObservableCollection<RouteRow> RecentRoutes { get; } =
		[];


	public bool HasRecentRoutes =>
		RecentRoutes.Count > 0;


	/// <summary>True while the list is cut short; the toggle then offers the rest.</summary>
	public bool CanExpandRoutes
	{
		get => field;

		private set => SetProperty(
			ref field,
			value);
	}


	public bool ShowAllRoutes
	{
		get => field;

		set
		{
			if (SetProperty(
					ref field,
					value))
			{
				RefreshRoutes();
			}
		}
	}


	public string RoutesToggleText =>
		ShowAllRoutes
			? _localization
				.CurrentStrings
				.Plan
				.ShowFewer
			: string.Format(
				CultureInfo.CurrentCulture,
				_localization
					.CurrentStrings
					.Plan
					.ShowAllSearches,
				_store.RecentRoutes.Count);


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


	public IconGlyph FromStar =>
		Star(From);


	public IconGlyph ToStar =>
		Star(To);


	public string GpsFromText =>
		_localization.CurrentStrings.Plan.UseCurrentLocationFrom;


	public string GpsToText =>
		_localization.CurrentStrings.Plan.UseCurrentLocationTo;


	public string TakeMeHomeText =>
		_localization.CurrentStrings.Plan.TakeMeHome;


	public string SaveCurrentRouteText =>
		_localization.CurrentStrings.Plan.SaveCurrentRoute;


	public string SavedRoutesTitle =>
		_localization.CurrentStrings.Plan.SavedRoutes;


	public string LoadRouteText =>
		_localization.CurrentStrings.Plan.LoadRoute;


	public string CurrentLocationText =>
		_localization.CurrentStrings.Plan.CurrentLocation;


	public bool IsGettingLocation
	{
		get => _isGettingLocation;
		private set => SetProperty(ref _isGettingLocation, value);
	}


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

				OnPropertyChanged(
					nameof(IsDeparture));
			}
		}
	}

	/// <summary>Inverse of <see cref="IsArrival"/>, for the "Leave" toggle.</summary>
	public bool IsDeparture => !IsArrival;


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


	public bool HasSavedRoutes =>
		SavedRoutes.Count > 0;


	public bool HasHome =>
		_store.HasHome;


	public bool IsPlacesEmpty =>
		!HasFavourites
		&& !HasRecents
		&& !HasRecentRoutes
		&& !HasSavedRoutes;


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

				// Stop ids belong to one provider: a switch invalidates start and destination.
				if (!string.Equals(_providerId, _providers.SelectedId, StringComparison.OrdinalIgnoreCase))
				{
					_providerId = _providers.SelectedId;
					From = null;
					To = null;
					OnPropertyChanged(nameof(IsTrackingAvailable));
				}

				MinDate =
					now.Date;

				OnPropertyChanged(
					nameof(MaxDate));

				// A chosen time in the past is a deliberate choice and stays; only a date the
				// picker can no longer show (before today) falls back to now.
				if (IsNow)
				{
					SetNow();
				}
				else if (_when.Date < now.Date)
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


	/// <summary>
	/// Fills the planner from an external request (see the contract). Only what the caller gave is
	/// changed; the rest keeps what the user had. The time was validated by the resolver.
	/// </summary>
	public void ApplyContract(ResolvedPlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);

		Safe(
			() =>
			{
				if (plan.From is { } from)
				{
					From = from;
				}

				if (plan.To is { } to)
				{
					To = to;
				}

				if (plan.Mode is { } mode)
				{
					IsArrival = mode == JourneySearchMode.Arrival;
				}

				if (plan.IsNow
					&& !IsArrival)
				{
					SetNow();
				}
				else if (plan.IsNow)
				{
					// "Arrive by now" means nothing; take the current minute as the arrival time.
					IsNow = false;
					Apply(Format.NowLocal());
				}
				else if (plan.When is { } when)
				{
					IsNow = false;
					Apply(when);
				}
			});
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

		// Past times are allowed: the user may look up a connection that has already left.
		DateTime target =
			IsNow
				? now
				: _when;

		return new JourneyQuery
		{
			From = From,
			To = To,
			DateTime =
				IsNow
					? Format.Now()
					: Format.ToOffset(target),
			SearchMode =
				IsArrival
					? JourneySearchMode.Arrival
					: JourneySearchMode.Departure,
			MaxResults =
				_settings.MaxResults,
			TimeoutSeconds =
				_settings.TimeoutSeconds,
			Routing =
				_settings.Routing
		};
	}


	private void SetWhen(
		DateTime candidate,
		bool fromTimeOfDay)
	{
		DateTime now =
			Format.NowLocal();

		// Convenience for the most common case only: on today's date, setting just the time to
		// one already gone means "the next time it is that time", i.e. tomorrow. Setting the date
		// (also back to today afterwards) is taken literally, so past times can be searched.
		if (fromTimeOfDay
			&& _when.Date == now.Date
			&& candidate.Date == now.Date
			&& now - candidate > RolloverGrace)
		{
			candidate =
				candidate.AddDays(1);
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

		// Not before today: the date picker could not show it.
		Apply(
			next.Date < now.Date
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
		if (!CanSearch || OpenResults is null)
		{
			return;
		}

		if (!TryBuildQuery(out JourneyQuery query))
		{
			return;
		}

		_store.AddRecent(query.To);
		_store.AddRecent(query.From);
		_store.AddRecentRoute(query.From, query.To);

		await OpenResults(query);
	}

	private bool TryBuildQuery(out JourneyQuery query)
	{
		query = default!;

		if (From is null || To is null)
		{
			return false;
		}

		DateTime now = Format.NowLocal();
		DateTime target = IsNow ? now : _when;

		query = new JourneyQuery
		{
			From = From,
			To = To,
			DateTime = IsNow ? Format.Now() : Format.ToOffset(target),
			SearchMode = IsArrival ? JourneySearchMode.Arrival : JourneySearchMode.Departure,
			MaxResults = _settings.MaxResults,
			TimeoutSeconds = _settings.TimeoutSeconds,
			Routing = _settings.Routing
		};

		return true;
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


	private IconGlyph Star(
		Location? place)
	{
		try
		{
			return place is not null
				&& _store.IsFavourite(place)
					? IconGlyph.StarFilled
					: IconGlyph.Star;
		}
		catch
		{
			return IconGlyph.Star;
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
			() =>
			{
				if (!IsDisposed)
				{
					RefreshPlaces();
					RefreshSavedRoutes();
				}
			});


	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					RefreshLocalizedProperties();
				}
			});


	/// <summary>Rebuilds the list of searched connections, honouring the "show all" toggle.</summary>
	private void RefreshRoutes()
	{
		IReadOnlyList<RoutePair> all =
			_store.RecentRoutes;

		IEnumerable<RoutePair> shown =
			ShowAllRoutes
				? all
				: all.Take(CollapsedRoutes);

		RecentRoutes.Clear();

		foreach (RoutePair route in shown)
		{
			RecentRoutes.Add(
				new RouteRow(route));
		}

		CanExpandRoutes =
			all.Count > CollapsedRoutes;

		if (!CanExpandRoutes
			&& ShowAllRoutes)
		{
			// The list shrank below the fold: fold it back without recursing.
			ShowAllRoutes = false;
		}

		OnPropertyChanged(
			nameof(HasRecentRoutes));

		OnPropertyChanged(
			nameof(RoutesToggleText));
	}


	private async Task UseRouteAsync(
		RouteRow? row)
	{
		if (row is null)
		{
			return;
		}

		From = row.Route.From;
		To = row.Route.To;

		if (CanSearch)
		{
			await SearchAsync();
		}
	}


	private async Task SetLocationFromGpsAsync(bool isFrom)
	{
		if (IsGettingLocation)
		{
			return;
		}

		// Check permission first
		if (CheckLocationPermission is not null)
		{
			bool hasPermission = await CheckLocationPermission();
			if (!hasPermission)
			{
				if (RequestLocationPermission is not null)
				{
					await RequestLocationPermission();
					// Check again after requesting
					hasPermission = await CheckLocationPermission();
				}
				if (!hasPermission)
				{
					ShowError?.Invoke(_localization.CurrentStrings.Plan.LocationUnavailable);
					return;
				}
			}
		}

		IsGettingLocation = true;

		try
		{
			Location? location = await _locationService.GetCurrentLocationAsync();

			if (location is not null)
			{
				if (isFrom)
				{
					From = location;
				}
				else
				{
					To = location;
				}

				if (CanSearch && OpenResults is not null && From is not null && To is not null)
				{
					if (!TryBuildQuery(out JourneyQuery query))
					{
						return;
					}

					_store.AddRecent(query.To);
					_store.AddRecent(query.From);
					_store.AddRecentRoute(query.From, query.To);

					await OpenResults(query);
				}
			}
			else
			{
				ShowError?.Invoke(_localization.CurrentStrings.Plan.LocationUnavailable);
			}
		}
		finally
		{
			IsGettingLocation = false;
		}
	}


	private async Task TakeMeHomeAsync()
	{
		SavedLocation? home = _store.Home;

		if (home is null)
		{
			ShowError?.Invoke(_localization.CurrentStrings.Plan.SetHomeLocation);
			return;
		}

		From = home.Location;

		if (CanSearch && OpenResults is not null && To is not null)
		{
			if (!TryBuildQuery(out JourneyQuery query))
			{
				return;
			}

			_store.AddRecent(query.To);
			_store.AddRecent(query.From);
			_store.AddRecentRoute(query.From, query.To);

			await OpenResults(query);
		}
	}


	private async Task LoadSavedRouteAsync(SavedRoute? route)
	{
		if (route is null)
		{
			return;
		}

		From = route.From;
		To = route.To;
		IsArrival = !route.IsDeparture;

		if (route.DefaultDateTime.HasValue)
		{
			IsNow = false;
			Apply(route.DefaultDateTime.Value);
		}

		if (CanSearch)
		{
			await SearchAsync();
		}
	}


	private async Task SaveCurrentRouteAsync()
	{
		if (From is null || To is null)
		{
			ShowError?.Invoke(_localization.CurrentStrings.Plan.StartAndDestinationRequired);
			return;
		}

		string routeName = await ShowRouteNameDialogAsync();

		if (string.IsNullOrWhiteSpace(routeName))
		{
			return;
		}

		if (_store.SavedRoutes.Any(r => r.Name == routeName))
		{
			ShowError?.Invoke(_localization.CurrentStrings.Plan.RouteNameExists);
			return;
		}

		var savedRoute = new SavedRoute(
			routeName,
			From,
			To,
			_settings.Routing,
			IsNow ? Format.NowLocal() : _when,
			!IsArrival);

		_store.AddSavedRoute(savedRoute);
	}


	private async Task SetHomeLocationAsync()
	{
		if (IsGettingLocation)
		{
			return;
		}

		IsGettingLocation = true;

		try
		{
			Location? location = await _locationService.GetCurrentLocationAsync();

			if (location is not null)
			{
				string name = await ShowHomeLocationNameDialogAsync();

				if (string.IsNullOrWhiteSpace(name))
				{
					name = _localization.CurrentStrings.Plan.HomeLocationName;
				}

				var savedLocation = new SavedLocation(name, location, true);
				_store.SetHome(savedLocation);

				_settings.SetHomeLocation(location, name);
			}
			else
			{
				ShowError?.Invoke(_localization.CurrentStrings.Plan.LocationUnavailable);
			}
		}
		finally
		{
			IsGettingLocation = false;
		}
	}


	private async Task<string> ShowRouteNameDialogAsync()
	{
		if (ShowRouteNameDialog is not null)
		{
			return await ShowRouteNameDialog(_localization.CurrentStrings.Plan.EnterRouteName);
		}
		return _localization.CurrentStrings.Plan.EnterRouteName;
	}


	private async Task<string> ShowHomeLocationNameDialogAsync()
	{
		if (ShowHomeLocationNameDialog is not null)
		{
			return await ShowHomeLocationNameDialog(_localization.CurrentStrings.Plan.HomeLocationName);
		}
		return _localization.CurrentStrings.Plan.HomeLocationName;
	}


	private void RefreshPlaces()
	{
		Safe(
			() =>
			{
				RefreshRoutes();

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


	private void RefreshSavedRoutes()
	{
		Safe(
			() =>
			{
				Replace(
					SavedRoutes,
					_store.SavedRoutes);

				OnPropertyChanged(
					nameof(HasSavedRoutes));

				OnPropertyChanged(
					nameof(HasHome));
			});
	}


	private void RefreshLocalizedProperties()
	{
		OnPropertyChanged(
			nameof(FromText));

		OnPropertyChanged(
			nameof(FromName));

		OnPropertyChanged(
			nameof(ToText));

		OnPropertyChanged(
			nameof(ToName));

		OnPropertyChanged(
			nameof(WhenText));

		OnPropertyChanged(
			nameof(ModeText));

		OnPropertyChanged(
			nameof(RoutesToggleText));
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


	private static void Replace(
		ObservableCollection<SavedRoute> target,
		IReadOnlyList<SavedRoute> source)
	{
		target.Clear();

		foreach (SavedRoute route in source)
		{
			target.Add(route);
		}
	}


	private static bool SamePlace(
		Location a,
		Location b) =>
		a.IsStation
			&& b.IsStation
			? string.Equals(
				a.StopKey,
				b.StopKey,
				StringComparison.OrdinalIgnoreCase)
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