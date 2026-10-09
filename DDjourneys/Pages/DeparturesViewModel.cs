using DDjourneys.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>One departure as the monitor lists it.</summary>
public sealed record DepartureRow(
	Departure Departure)
{
	public string LineText =>
		string.IsNullOrWhiteSpace(Departure.Line.Name)
			? Format.TransportMode(Departure.Line.Mode)
			: Departure.Line.Name;

	// Once per row: the chip look is bound several times, and the line never changes for a departure.
	public ChipLook Look { get; } = ModeChips.For(Departure.Line.Mode);

	// What the "in 5 min" text said when the row was made: a kept row is only kept while it would still say the same.
	private readonly string? _shownIn = InTextOf(Departure);

	/// <summary>True when <paramref name="other"/> would look exactly like this row (the row can stay as it is).</summary>
	public bool Shows(DepartureRow other)
	{
		Departure a = Departure;
		Departure b = other.Departure;

		return ReferenceEquals(a, b)
			|| (a.Id == b.Id
				&& a.StopId == b.StopId
				&& a.IsArrival == b.IsArrival
				&& a.Scheduled == b.Scheduled
				&& a.Realtime == b.Realtime
				&& a.Platform == b.Platform
				&& a.PlatformKind == b.PlatformKind
				&& a.State == b.State
				&& a.Occupancy == b.Occupancy
				&& a.Line.Name == b.Line.Name
				&& a.Line.Mode == b.Line.Mode
				&& a.Line.Destination == b.Line.Destination
				&& a.RouteChangeIds.SequenceEqual(b.RouteChangeIds)
				&& _shownIn == other._shownIn);
	}

	public string Direction =>
		Departure.Line.Destination ?? string.Empty;

	public string TimeText =>
		Format.Time(Departure.Effective);

	public string? DelayText =>
		Departure.IsCancelled
			? null
			: Format.Delay(Departure.Delay);

	public bool HasDelay =>
		DelayText is not null;

	public bool IsEarly =>
		Departure.Delay is { } delay && delay < TimeSpan.Zero;

	public string PlannedText =>
		Format.Time(Departure.Scheduled);

	/// <summary>"in 5 min" / "now" for what leaves within the hour.</summary>
	public string? InText => InTextOf(Departure);

	private static string? InTextOf(Departure departure)
	{
		if (departure.IsCancelled)
		{
			return null;
		}

		double minutes =
			(departure.Effective - Format.Now()).TotalMinutes;

		if (minutes is < -1 or > 60)
		{
			return null;
		}

		TrackingStrings strings =
			LocalizationService.Current.CurrentStrings.Tracking;

		return minutes < 1
			? strings.CourseNow
			: string.Format(
				CultureInfo.CurrentCulture,
				strings.CourseIn,
				(int)Math.Round(minutes));
	}

	public bool HasIn =>
		InText is not null;

	public string? PlatformText
	{
		get
		{
			if (string.IsNullOrWhiteSpace(Departure.Platform))
			{
				return null;
			}

			TrackingStrings strings =
				LocalizationService.Current.CurrentStrings.Tracking;

			return string.Format(
				CultureInfo.CurrentCulture,
				Departure.PlatformKind == PlatformKind.Railtrack
					? strings.CourseTrack
					: strings.CoursePlatform,
				Departure.Platform);
		}
	}

	public bool HasPlatform =>
		PlatformText is not null;

	public bool IsCancelled =>
		Departure.IsCancelled;

	private static string CancelledText =>
		LocalizationService.Current.CurrentStrings.Departures.Cancelled;

	public OccupancyLevel Occupancy =>
		Departure.Occupancy;

	public bool HasOccupancy =>
		Occupancy != OccupancyLevel.Unknown;

	public bool HasChanges =>
		Departure.HasRouteChanges;

	public string Description =>
		$"{LineText} {Direction}, {TimeText}"
		+ (DelayText is { } delay ? $", {delay}" : string.Empty)
		+ (IsCancelled ? $", {CancelledText}" : string.Empty);
}


/// <summary>A stop near the passenger.</summary>
public sealed record NearbyRow(
	NearbyStop Nearby)
{
	public Location Stop =>
		Nearby.Stop;

	public string Name =>
		Nearby.Stop.Name;

	public string? Place =>
		StopLabel.PlaceFor(
			Nearby.Stop.Name,
			Nearby.Stop.Place);

	public string DistanceText =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.Departures.Metres,
			Nearby.DistanceMeters);
}


/// <summary>A line that serves the chosen stop.</summary>
public sealed record StopLineRow(
	StopLine Line)
{
	public string LineText =>
		Line.Name;

	public ChipLook Look =>
		ModeChips.For(Line.Mode);

	public string DirectionsText =>
		string.Join(
			"\n",
			Line.Directions.Select(direction => direction.Name));

	public bool HasChanges =>
		Line.RouteChangeIds.Count > 0;
}


/// <summary>One platform with its accessibility data (Dresden open data).</summary>
public sealed record AccessRow(
	StopAccessibility Data)
{
	private static string? Line(string format, string? value) =>
		string.IsNullOrWhiteSpace(value)
			? null
			: string.Format(CultureInfo.CurrentCulture, format, value);

	public string Title =>
		Line(
			LocalizationService.Current.CurrentStrings.Extras.AccessPlatform,
			Data.Platform)
		?? Data.StopName;

	/// <summary>The remaining facts, one per line.</summary>
	public string Details
	{
		get
		{
			ExtrasStrings strings =
				LocalizationService.Current.CurrentStrings.Extras;

			return string.Join(
				"\n",
				new[]
				{
					Line(strings.AccessBoarding, Data.Boarding),
					Line(strings.AccessKerb, Data.KerbHeight),
					Line(strings.AccessWidth, Data.Width),
					Line(strings.AccessTactile, Data.TactileGuidance),
					Line(strings.AccessAudio, Data.AudioAnnouncements)
				}
				.Where(line => line is not null));
		}
	}
}


/// <summary>A DVB service point near the stop or the passenger.</summary>
public sealed record ServicePointRow(
	ServicePoint Point)
{
	public string Name =>
		Point.Name;

	public string Details =>
		string.Join(
			"\n",
			Point.Details
				.Take(4)
				.Select(detail => detail.Value));

	public bool HasDetails =>
		Details.Length > 0;

	public string DistanceText =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.Departures.Metres,
			Point.DistanceMeters);

	public Uri MapUri =>
		new(
			string.Create(
				CultureInfo.InvariantCulture,
				$"https://www.openstreetmap.org/?mlat={Point.Latitude:F6}&mlon={Point.Longitude:F6}#map=17/{Point.Latitude:F6}/{Point.Longitude:F6}"));
}


/// <summary>
/// Departure monitor: what leaves (or arrives at) a stop, optionally at another time, with the lines of the
/// stop, the tariff zone and the stops near the passenger.
/// </summary>
public sealed partial class DeparturesViewModel : DisposableViewModel
{
	private const int NearbyRadius = 600;

	private readonly DepartureService _departures;
	private readonly NetworkService _network;
	private readonly OpenDataService _openData;
	private readonly LocationService _locations;
	private readonly DeviceLocator _locator;
	private readonly PlaceStore _store;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	private DateTime _when;
	private bool _syncing;
	private bool _isNow = true;
	private bool _linesLoaded;
	private Location? _accessibilityFor;
	private bool _servicePointsLoaded;
	private (double Latitude, double Longitude)? _here;
	private IReadOnlyList<Departure> _current = [];
	private CancellationTokenSource? _refresh;
	private CancellationTokenSource? _info;

	public DeparturesViewModel(
		DepartureService departures,
		NetworkService network,
		OpenDataService openData,
		LocationService locations,
		DeviceLocator locator,
		PlaceStore store,
		AppSettings settings,
		ProviderRegistry providers)
	{
		ArgumentNullException.ThrowIfNull(providers);

		ArgumentNullException.ThrowIfNull(departures);
		ArgumentNullException.ThrowIfNull(network);
		ArgumentNullException.ThrowIfNull(openData);
		ArgumentNullException.ThrowIfNull(locations);
		ArgumentNullException.ThrowIfNull(locator);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);

		_departures = departures;
		_network = network;
		_openData = openData;
		_locations = locations;
		_locator = locator;
		_store = store;
		_settings = settings;
		_localization = LocalizationService.Current;
		_when = Format.NowLocal();

		ListenToLocalization(
			_localization,
			OnLocalizationChanged);

		Subscribe(
			() => providers.SelectionChanged += OnProviderChanged,
			() => providers.SelectionChanged -= OnProviderChanged);

		PickStopCommand =
			new AsyncCommand(
				() => OpenPlaceSearch is { } open
					? open()
					: Task.CompletedTask);

		RefreshCommand =
			new AsyncCommand(
				() => RefreshAsync());

		LocateCommand =
			new AsyncCommand(
				LocateAsync,
				() => !IsLocating);

		SelectStopCommand =
			new Command<Location>(
				stop =>
				{
					if (stop is not null)
					{
						SetStop(stop);
					}
				});

		SelectNearbyCommand =
			new Command<NearbyRow>(
				row =>
				{
					if (row is not null)
					{
						SetStop(row.Stop);
					}
				});

		DepartCommand =
			new Command(
				() => SetMode(false));

		ArriveCommand =
			new Command(
				() => SetMode(true));

		NowCommand =
			new Command(
				SetNow);

		OpenRunCommand =
			new AsyncCommand<DepartureRow>(
				row => row is not null && OpenRun is { } open
					? open(row.Departure)
					: Task.CompletedTask);

		OpenChangesCommand =
			new AsyncCommand<DepartureRow>(
				row => row is not null && OpenChanges is { } open
					? open(row.Departure)
					: Task.CompletedTask);

		ToggleLinesCommand =
			new AsyncCommand(
				ToggleLinesAsync);

		ToggleAccessibilityCommand =
			new AsyncCommand(
				ToggleAccessibilityAsync);

		ToggleServicePointsCommand =
			new AsyncCommand(
				ToggleServicePointsAsync);

		OpenServicePointCommand =
			new AsyncCommand<ServicePointRow>(
				OpenServicePointAsync);

		OpenMapCommand =
			new AsyncCommand(
				OpenMapAsync);

		RefreshQuickPicks();
	}

	public Func<Task>? OpenPlaceSearch { get; set; }

	public Func<Departure, Task>? OpenRun { get; set; }

	/// <summary>Opens the disruptions of the departure's line.</summary>
	public Func<Departure, Task>? OpenChanges { get; set; }

	public AsyncCommand PickStopCommand { get; }

	public AsyncCommand RefreshCommand { get; }

	public AsyncCommand LocateCommand { get; }

	public Command<Location> SelectStopCommand { get; }

	public Command<NearbyRow> SelectNearbyCommand { get; }

	public Command DepartCommand { get; }

	public Command ArriveCommand { get; }

	public Command NowCommand { get; }

	public AsyncCommand<DepartureRow> OpenRunCommand { get; }

	public AsyncCommand<DepartureRow> OpenChangesCommand { get; }

	public AsyncCommand ToggleLinesCommand { get; }

	public AsyncCommand ToggleAccessibilityCommand { get; }

	public AsyncCommand ToggleServicePointsCommand { get; }

	public AsyncCommand<ServicePointRow> OpenServicePointCommand { get; }

	/// <summary>Shows the stop, the stops near the passenger and the service points on a map.</summary>
	public AsyncCommand OpenMapCommand { get; }

	public ObservableCollection<AccessRow> Accessibility { get; } = [];

	public ObservableCollection<ServicePointRow> ServicePoints { get; } = [];

	public ObservableCollection<DepartureRow> Rows { get; } = [];

	public ObservableCollection<NearbyRow> Nearby { get; } = [];

	public ObservableCollection<StopLineRow> Lines { get; } = [];

	public ObservableCollection<Location> QuickPicks { get; } = [];

	public Location? Stop
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(StopName));
				OnPropertyChanged(nameof(StopPlace));
				OnPropertyChanged(nameof(HasStop));
				OnPropertyChanged(nameof(NoStop));
				OnPropertyChanged(nameof(ShowQuickPicks));
			}
		}
	}

	public bool HasStop =>
		Stop is not null;

	public bool NoStop =>
		Stop is null;

	public string StopName =>
		Stop?.Name
		?? _localization.CurrentStrings.Departures.ChooseStop;

	public string? StopPlace =>
		Stop is null
			? null
			: StopLabel.PlaceFor(
				Stop.Name,
				Stop.Place);

	public bool IsArrival
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(IsDeparture));
			}
		}
	}

	public bool IsDeparture =>
		!IsArrival;

	public bool IsNow
	{
		get => _isNow;

		private set
		{
			if (SetProperty(ref _isNow, value))
			{
				OnPropertyChanged(nameof(IsNotNow));
			}
		}
	}

	public bool IsNotNow =>
		!IsNow;

	public DateTime Date
	{
		get => _when.Date;

		set
		{
			if (_syncing || value.Date == _when.Date)
			{
				return;
			}

			_when = value.Date + _when.TimeOfDay;
			WhenChanged();
		}
	}

	public TimeSpan Time
	{
		get => new(_when.Hour, _when.Minute, 0);

		set
		{
			if (_syncing
				|| (value.Hours == _when.Hour && value.Minutes == _when.Minute))
			{
				return;
			}

			_when = _when.Date + new TimeSpan(value.Hours, value.Minutes, 0);
			WhenChanged();
		}
	}

	public bool IsBusy
	{
		get;
		private set => SetProperty(ref field, value);
	}

	public bool IsLocating
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				LocateCommand.RaiseCanExecuteChanged();
			}
		}
	}

	public string Message
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasMessage));
			}
		}
	} = string.Empty;

	public bool HasMessage =>
		Message.Length > 0;

	public string UpdatedText
	{
		get;
		private set => SetProperty(ref field, value);
	} = string.Empty;

	public string? ZoneText
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasZone));
			}
		}
	}

	public bool HasZone =>
		ZoneText is not null;

	public bool HasNearby =>
		Nearby.Count > 0;

	public bool HasQuickPicks =>
		QuickPicks.Count > 0;

	public bool ShowQuickPicks =>
		NoStop && HasQuickPicks;

	public bool ShowHint =>
		NoStop && !HasNearby;

	public bool ShowLines
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(LinesToggleText));
			}
		}
	}

	public string LinesToggleText =>
		ShowLines
			? _localization.CurrentStrings.Departures.HideLines
			: _localization.CurrentStrings.Departures.ShowLines;

	public bool HasLines =>
		Lines.Count > 0;

	public bool ShowAccessibility
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(AccessibilityToggleText));
			}
		}
	}

	public string AccessibilityToggleText =>
		ShowAccessibility
			? _localization.CurrentStrings.Extras.AccessHide
			: _localization.CurrentStrings.Extras.AccessTitle;

	/// <summary>What the lines section holds: how many lines serve the stop (once known).</summary>
	public string LinesSummary =>
		Lines.Count == 0
			? string.Empty
			: string.Format(
				CultureInfo.CurrentCulture,
				_localization.CurrentStrings.Extras.LinesCount,
				Lines.Count);

	public string ServicePointsSummary =>
		_servicePointsLoaded && ServicePoints.Count > 0
			? string.Format(
				CultureInfo.CurrentCulture,
				_localization.CurrentStrings.Extras.ServiceCount,
				ServicePoints.Count)
			: _localization.CurrentStrings.Extras.ServiceHint;

	public string AccessibilityMessage
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasAccessibilityMessage));
			}
		}
	} = string.Empty;

	public bool HasAccessibilityMessage =>
		AccessibilityMessage.Length > 0;

	public bool ShowServicePoints
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(ServicePointsToggleText));
			}
		}
	}

	public string ServicePointsToggleText =>
		ShowServicePoints
			? _localization.CurrentStrings.Extras.ServiceHide
			: _localization.CurrentStrings.Extras.ServiceShow;

	public string ServicePointsMessage
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasServicePointsMessage));
			}
		}
	} = string.Empty;

	public bool HasServicePointsMessage =>
		ServicePointsMessage.Length > 0;

	/// <summary>
	/// Stop ids, lines and boards belong to one provider: after a switch nothing of the old one is kept, and the
	/// quick picks are those of the new provider's favourites and recents.
	/// </summary>
	private void OnProviderChanged(object? sender, string providerId) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (IsDisposed)
				{
					return;
				}

				_refresh?.Cancel();
				_info?.Cancel();

				Stop = null;
				_current = [];
				Rows.Clear();
				Nearby.Clear();
				Lines.Clear();
				_linesLoaded = false;
				Accessibility.Clear();
				AccessibilityMessage = string.Empty;
				_accessibilityFor = null;
				ServicePoints.Clear();
				ServicePointsMessage = string.Empty;
				_servicePointsLoaded = false;
				ZoneText = null;
				Message = string.Empty;

				OnPropertyChanged(nameof(HasLines));
				OnPropertyChanged(nameof(LinesSummary));
				OnPropertyChanged(nameof(ServicePointsSummary));

				RefreshQuickPicks();
			});

	/// <summary>Called by the page after a place search for this page.</summary>
	public void SetStop(Location stop)
	{
		ArgumentNullException.ThrowIfNull(stop);

		if (!stop.IsStation)
		{
			return;
		}

		try
		{
			_store.AddRecent(stop);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Remembering the place failed: {ex.Message}");
		}

		Stop = stop;
		_current = [];
		Rows.Clear();
		Lines.Clear();
		_linesLoaded = false;
		Accessibility.Clear();
		AccessibilityMessage = string.Empty;
		_accessibilityFor = null;
		ServicePoints.Clear();
		ServicePointsMessage = string.Empty;
		_servicePointsLoaded = false;
		ZoneText = null;
		Message = string.Empty;
		OnPropertyChanged(nameof(HasLines));
		OnPropertyChanged(nameof(LinesSummary));
		OnPropertyChanged(nameof(ServicePointsSummary));

		_ = RefreshAsync();
		_ = LoadStopInfoAsync(stop);

		// Sections the passenger left open follow the new stop.
		if (ShowAccessibility)
		{
			_ = LoadAccessibilityAsync(stop);
		}

		if (ShowServicePoints)
		{
			_ = LoadServicePointsAsync();
		}
	}

	/// <summary>Reloads the departures. <paramref name="silent"/>: no busy indicator, and an error keeps what is shown.</summary>
	public async Task RefreshAsync(bool silent = false)
	{
		if (Stop is not { } stop || IsDisposed)
		{
			return;
		}

		_refresh?.Cancel();

		var cts = new CancellationTokenSource();
		_refresh = cts;

		if (!silent)
		{
			IsBusy = true;
		}

		try
		{
			if (IsNow)
			{
				SetWhen(Format.NowLocal());
			}

			DepartureBoard board =
				await _departures.GetDeparturesAsync(
					new DepartureQuery
					{
						Stop = stop,
						Time = IsNow ? null : Format.ToOffset(_when),
						IsArrival = IsArrival,
						Limit = 25,
						TimeoutSeconds = _settings.TimeoutSeconds
					},
					cts.Token);

			if (cts.IsCancellationRequested || IsDisposed)
			{
				return;
			}

			_current = board.Departures;
			RebuildRows();

			Message =
				_current.Count == 0
					? _localization.CurrentStrings.Departures.NoDepartures
					: string.Empty;

			UpdatedText =
				string.Format(
					CultureInfo.CurrentCulture,
					_localization.CurrentStrings.Departures.Updated,
					Format.Time(Format.Now()));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Departures failed: {ex}");

			if (!silent || Rows.Count == 0)
			{
				Message =
					string.IsNullOrWhiteSpace(ex.Message)
						? _localization.CurrentStrings.Common.SomethingWentWrong
						: ex.Message;
			}
		}
		finally
		{
			if (ReferenceEquals(_refresh, cts))
			{
				IsBusy = false;
			}
		}
	}

	/// <summary>Stops near the device; the nearest becomes the stop when none is chosen yet.</summary>
	private async Task LocateAsync()
	{
		if (IsLocating)
		{
			return;
		}

		IsLocating = true;

		try
		{
			(double Latitude, double Longitude)? position =
				await _locator.LocateAsync();

			if (position is not { } here)
			{
				Message =
					_locator.Failure == DeviceLocationFailure.PermissionDenied
						? _localization.CurrentStrings.Plan.LocationPermissionDenied
						: _localization.CurrentStrings.Plan.LocationUnavailable;

				return;
			}

			_here = here;

			IReadOnlyList<NearbyStop> found =
				await _network.GetNearbyStopsAsync(
					here.Latitude,
					here.Longitude,
					NearbyRadius);

			if (found.Count == 0)
			{
				// The map has no pins for it: the stop search around the position still names the nearest.
				IReadOnlyList<Location> stops =
					await _locations.SearchByCoordinatesAsync(
						here.Latitude,
						here.Longitude,
						timeout: TimeSpan.FromSeconds(_settings.TimeoutSeconds));

				found =
					(List<NearbyStop>)
					[.. stops
						.Take(10)
						.Select(
							stop =>
								new NearbyStop
								{
									Stop = stop,
									DistanceMeters =
										stop.Latitude is { } latitude && stop.Longitude is { } longitude
											? (int)Math.Round(
												GeoMath.DistanceMeters(here.Latitude, here.Longitude, latitude, longitude))
											: 0
								})];
			}

			Nearby.Clear();

			foreach (NearbyStop stop in found)
			{
				Nearby.Add(new NearbyRow(stop));
			}

			OnPropertyChanged(nameof(HasNearby));
			OnPropertyChanged(nameof(ShowHint));

			if (found.Count == 0)
			{
				Message = _localization.CurrentStrings.Plan.NoStopNearby;
			}
			else if (Stop is null)
			{
				SetStop(found[0].Stop);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Locating failed: {ex}");

			Message =
				string.IsNullOrWhiteSpace(ex.Message)
					? _localization.CurrentStrings.Common.SomethingWentWrong
					: ex.Message;
		}
		finally
		{
			IsLocating = false;
		}
	}

	private async Task ToggleAccessibilityAsync()
	{
		ShowAccessibility = !ShowAccessibility;

		if (ShowAccessibility
			&& Stop is { } stop
			&& !ReferenceEquals(_accessibilityFor, stop))
		{
			await LoadAccessibilityAsync(stop);
		}
	}

	private async Task LoadAccessibilityAsync(Location stop)
	{
		AccessibilityMessage = _localization.CurrentStrings.Common.Loading;

		try
		{
			IReadOnlyList<StopAccessibility> entries =
				await _openData.GetStopAccessibilityAsync(
					stop,
					timeout: TimeSpan.FromSeconds(_settings.TimeoutSeconds));

			if (!ReferenceEquals(Stop, stop) || IsDisposed)
			{
				return;
			}

			_accessibilityFor = stop;
			Accessibility.Clear();

			foreach (StopAccessibility entry in entries)
			{
				Accessibility.Add(new AccessRow(entry));
			}

			AccessibilityMessage =
				entries.Count == 0
					? _localization.CurrentStrings.Extras.AccessNone
					: string.Empty;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Accessibility failed: {ex.Message}");

			AccessibilityMessage = _localization.CurrentStrings.Common.SomethingWentWrong;
		}
	}

	private async Task ToggleServicePointsAsync()
	{
		ShowServicePoints = !ShowServicePoints;

		if (ShowServicePoints
			&& !_servicePointsLoaded)
		{
			await LoadServicePointsAsync();
		}
	}

	private async Task LoadServicePointsAsync()
	{
		(double Latitude, double Longitude)? around =
			Stop is { Latitude: { } latitude, Longitude: { } longitude }
				? (latitude, longitude)
				: _here;

		if (around is not { } center)
		{
			ServicePointsMessage = _localization.CurrentStrings.Extras.ServiceNeedsPosition;

			return;
		}

		ServicePointsMessage = _localization.CurrentStrings.Common.Loading;

		Location? forStop = Stop;

		try
		{
			IReadOnlyList<ServicePoint> points =
				await _openData.GetServicePointsAsync(
					center.Latitude,
					center.Longitude,
					timeout: TimeSpan.FromSeconds(_settings.TimeoutSeconds));

			if (IsDisposed
				|| !ReferenceEquals(Stop, forStop))
			{
				return;
			}

			ServicePoints.Clear();

			foreach (ServicePoint point in points)
			{
				ServicePoints.Add(new ServicePointRow(point));
			}

			_servicePointsLoaded = true;

			ServicePointsMessage =
				points.Count == 0
					? _localization.CurrentStrings.Extras.ServiceNone
					: string.Empty;

			OnPropertyChanged(nameof(ServicePointsSummary));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Service points failed: {ex.Message}");

			ServicePointsMessage = _localization.CurrentStrings.Common.SomethingWentWrong;
		}
	}

	private async Task OpenServicePointAsync(ServicePointRow row)
	{
		try
		{
			await MapScenes.OpenAsync(
				MapScenes.FromStops(null, (Location[])[], (ServicePoint[])[row.Point]),
				row.Name);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening the map failed: {ex.Message}");
		}
	}

	private async Task OpenMapAsync()
	{
		try
		{
			ExtrasStrings strings = _localization.CurrentStrings.Extras;

			bool shown =
				await MapScenes.OpenAsync(
					MapScenes.FromStops(
						Stop,
						Nearby.Select(row => row.Stop),
						ShowServicePoints
							? ServicePoints.Select(row => row.Point)
							: []),
					Stop?.Name ?? strings.MapStopTitle);

			if (!shown)
			{
				Message = strings.MapNoData;
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening the map failed: {ex.Message}");
		}
	}

	private async Task ToggleLinesAsync()
	{
		ShowLines = !ShowLines;

		if (ShowLines
			&& !_linesLoaded
			&& Stop is { } stop)
		{
			await LoadLinesAsync(stop);
		}
	}

	private async Task LoadStopInfoAsync(Location stop)
	{
		_info?.Cancel();

		var cts = new CancellationTokenSource();
		_info = cts;

		try
		{
			if (ShowLines)
			{
				_ = LoadLinesAsync(stop);
			}

			if (stop.Latitude is { } latitude
				&& stop.Longitude is { } longitude)
			{
				TariffZone? zone =
					await _network.FindTariffZoneAsync(latitude, longitude, cts.Token);

				if (!cts.IsCancellationRequested
					&& ReferenceEquals(Stop, stop))
				{
					ZoneText =
						zone is null
							? null
							: string.Format(
								CultureInfo.CurrentCulture,
								_localization.CurrentStrings.Departures.TariffZone,
								zone);
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			// The zone is a nicety: the departures do not depend on it.
			DiagnosticLog.Write($"Tariff zone failed: {ex.Message}");
		}
	}

	private async Task LoadLinesAsync(Location stop)
	{
		try
		{
			IReadOnlyList<StopLine> lines =
				await _network.GetStopLinesAsync(stop);

			if (!ReferenceEquals(Stop, stop) || IsDisposed)
			{
				return;
			}

			Lines.Clear();

			foreach (StopLine line in lines.OrderBy(line => line.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				Lines.Add(new StopLineRow(line));
			}

			_linesLoaded = true;
			OnPropertyChanged(nameof(HasLines));
			OnPropertyChanged(nameof(LinesSummary));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Stop lines failed: {ex.Message}");
		}
	}

	/// <summary>Arrivals instead of departures and/or a time other than now, before a stop is set (which then loads).</summary>
	public void ApplyBoard(bool arrivals, DateTime? when)
	{
		IsArrival = arrivals;

		if (when is { } time)
		{
			IsNow = false;
			SetWhen(time);
		}
	}

	private void SetMode(bool arrival)
	{
		if (IsArrival == arrival)
		{
			return;
		}

		IsArrival = arrival;
		_ = RefreshAsync();
	}

	private void SetNow()
	{
		IsNow = true;
		SetWhen(Format.NowLocal());
		_ = RefreshAsync();
	}

	private void WhenChanged()
	{
		IsNow = false;
		OnPropertyChanged(nameof(Date));
		OnPropertyChanged(nameof(Time));
		_ = RefreshAsync();
	}

	private void SetWhen(DateTime value)
	{
		_syncing = true;

		try
		{
			_when = value;
			OnPropertyChanged(nameof(Date));
			OnPropertyChanged(nameof(Time));
		}
		finally
		{
			_syncing = false;
		}
	}

	private void RebuildRows()
	{
		// The board is refreshed every half minute and hardly changes: rows that show the same stay as they are.
		CollectionSync.Merge(
			Rows,
			[.. _current.Select(departure => new DepartureRow(departure))],
			static (shown, next) => shown.Shows(next));
	}

	/// <summary>Favourites first, then recently used stops (stops only: places without a stop id have no departures).</summary>
	public void RefreshQuickPicks()
	{
		QuickPicks.Clear();

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (Location place in _store.Favourites.Concat(_store.Recents))
		{
			if (place.IsStation
				&& place.StopKey is { } key
				&& seen.Add(key))
			{
				QuickPicks.Add(place);
			}

			if (QuickPicks.Count >= 8)
			{
				break;
			}
		}

		OnPropertyChanged(nameof(HasQuickPicks));
		OnPropertyChanged(nameof(ShowQuickPicks));
	}

	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (IsDisposed)
				{
					return;
				}

				OnPropertyChanged(nameof(StopName));
				OnPropertyChanged(nameof(LinesToggleText));
				OnPropertyChanged(nameof(AccessibilityToggleText));
				OnPropertyChanged(nameof(ServicePointsToggleText));
				OnPropertyChanged(nameof(LinesSummary));
				OnPropertyChanged(nameof(ServicePointsSummary));
				RebuildRows();
			});

	protected override void OnDisposing()
	{
		_refresh?.Cancel();
		_info?.Cancel();
	}
}
