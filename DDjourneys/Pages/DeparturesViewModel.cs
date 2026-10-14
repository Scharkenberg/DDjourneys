using DDjourneys.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Controls;
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

	/// <summary>Cancelled rows are struck through when the "show cancelled" toggle lets them through.</summary>
	public TextDecorations TextDecorations =>
		Departure.IsCancelled
			? TextDecorations.Strikethrough
			: TextDecorations.None;

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


/// <summary>One next departure of a nearby row: the line chip and how far away it leaves.</summary>
public sealed record NearbyPreview(Departure Departure)
{
	public string LineText =>
		string.IsNullOrWhiteSpace(Departure.Line.Name)
			? Format.TransportMode(Departure.Line.Mode)
			: Departure.Line.Name;

	public ChipLook Look =>
		ModeChips.For(Departure.Line.Mode);

	/// <summary>"3′" — the minutes until it leaves ("now" when it is this minute; ceil, never 0′).</summary>
	public string TimeText
	{
		get
		{
			DeparturesStrings strings =
				LocalizationService.Current.CurrentStrings.Departures;

			double minutes =
				(Departure.Effective - Format.Now()).TotalMinutes;

			return minutes < 1
				? strings.NowShort
				: string.Format(
					CultureInfo.CurrentCulture,
					strings.InMinutesShort,
					(int)Math.Ceiling(minutes));
		}
	}
}

/// <summary>A stop near the passenger, with the next departures it previews (loaded one row at a time).</summary>
public sealed partial class NearbyRow(
	NearbyStop nearby) : ObservableObject
{
	public NearbyStop Nearby =>
		nearby;

	public Location Stop =>
		nearby.Stop;

	public string Name =>
		nearby.Stop.Name;

	public string? Place =>
		StopLabel.PlaceFor(
			nearby.Stop.Name,
			nearby.Stop.Place);

	public string DistanceText =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.Departures.Metres,
			nearby.DistanceMeters);

	/// <summary>Up to three next departures, once the row's board has arrived (one request per row, staggered).</summary>
	public IReadOnlyList<NearbyPreview> Preview
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasPreview));
				OnPropertyChanged(nameof(ShownPreview));
			}
		}
	} = [];

	/// <summary>The row's board has arrived: its next departures (fed by the page's staggered loader).</summary>
	public void Adopt(IReadOnlyList<NearbyPreview> departures) =>
			Preview = departures;

	public bool HasPreview =>
		Preview.Count > 0;

	/// <summary>Collapsed shows one departure; expanded up to three.</summary>
	public bool IsExpanded
	{
		get;

		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(ShownPreview));
			}
		}
	}

	public IReadOnlyList<NearbyPreview> ShownPreview =>
		(List<NearbyPreview>)[.. Preview.Take(IsExpanded ? 3 : 1)];
}


/// <summary>
/// A line that serves the chosen stop. Carries the watch state of B4: the star beside the row watches the line
/// for disruptions (<see cref="Support.WatchedLines"/>, provider-scoped).
/// </summary>
public sealed partial class StopLineRow(
	StopLine line) : ObservableObject
{
	public StopLine Line =>
		line;

	public string LineText =>
		line.Name;

	public ChipLook Look =>
		ModeChips.For(line.Mode);

	public string DirectionsText =>
		string.Join(
			"\n",
			line.Directions.Select(direction => direction.Name));

	public bool HasChanges =>
		line.RouteChangeIds.Count > 0;

	/// <summary>Whether the passenger watches this line for disruptions (the star is filled then).</summary>
	public bool IsWatched
	{
		get;

		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(WatchStar));
				OnPropertyChanged(nameof(WatchDescription));
			}
		}
	}

	public IconGlyph WatchStar =>
		IsWatched ? IconGlyph.StarFilled : IconGlyph.Star;

	/// <summary>"Watch line 11" / "Stop watching line 11": what the star says to a screen reader.</summary>
	public string WatchDescription =>
		string.Format(
			CultureInfo.CurrentCulture,
			IsWatched
				? LocalizationService.Current.CurrentStrings.WatchedLines.UnwatchLine
				: LocalizationService.Current.CurrentStrings.WatchedLines.WatchLine,
			LineText);
}



/// <summary>One line of the filter chip row above the board: a tap toggles it, a long press keeps only it.</summary>
public sealed partial class LineFilterChip(
	string name,
	string key,
	Action<LineFilterChip> toggle,
	Action<LineFilterChip> only) : ObservableObject
{
	/// <summary>Own commands: a behavior in a template cannot bind through a relative source (it is no Element).</summary>
	public Command ToggleCommand =>
		field ??= new Command(() => toggle(this));

	public Command OnlyCommand =>
		field ??= new Command(() => only(this));

	public string Name =>
		name;

	/// <summary>The line's key in the filter (<see cref="BoardFilter.LineKey"/>).</summary>
	public string Key =>
		key;

	/// <summary>"Line 11": what the chip says to a screen reader.</summary>
	public string Description =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.Departures.LineChipDescription,
			name);

	public bool IsSelected
	{
		get;

		set => SetProperty(ref field, value);
	}
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
}


/// <summary>
/// Departure monitor: what leaves (or arrives at) a stop, optionally at another time, with the lines of the
/// stop, the tariff zone and the stops near the passenger.
/// </summary>
public sealed partial class DeparturesViewModel : DisposableViewModel
{
	private const int NearbyRadius = 600;

	/// <summary>Nearby rows that preview their next departures per pass (a request cap; the nearest first).</summary>
	private const int NearbyPreviewStops = 8;

	/// <summary>Departures a nearby row previews when expanded (one when collapsed).</summary>
	private const int NearbyPreviewRows = 3;

	private readonly DepartureService _departures;
	private readonly LineCourseService _lineCourses;
	private readonly NetworkService _network;
	private readonly OpenDataService _openData;
	private readonly LocationService _locations;
	private readonly DeviceLocator _locator;
	private readonly PlaceStore _store;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;
	private readonly ProviderRegistry _providers;

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
	private CancellationTokenSource? _previews;

	// The board filter: the lines picked in the chip row (their keys, see BoardFilter.LineKey) and the
	// cancelled toggle below them. Nothing is picked by default: the board looks exactly as it came.
	private readonly HashSet<string> _selectedLines = new(StringComparer.Ordinal);

	public DeparturesViewModel(
		DepartureService departures,
		LineCourseService lineCourses,
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
		ArgumentNullException.ThrowIfNull(lineCourses);
		ArgumentNullException.ThrowIfNull(network);
		ArgumentNullException.ThrowIfNull(openData);
		ArgumentNullException.ThrowIfNull(locations);
		ArgumentNullException.ThrowIfNull(locator);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);

		_departures = departures;
		_lineCourses = lineCourses;
		_network = network;
		_openData = openData;
		_locations = locations;
		_locator = locator;
		_store = store;
		_settings = settings;
		_localization = LocalizationService.Current;
		_providers = providers;
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

		OpenLineMapCommand =
			new AsyncCommand<StopLineRow>(
				row => row is not null && Stop is not null
					? OpenLineMapAsync(row)
					: Task.CompletedTask);

		ClearLineFilterCommand =
			new Command(ClearLineFilter);

		ToggleCancelledCommand =
			new Command(
				() => ShowCancelled = !ShowCancelled);

		ExpandNearbyCommand =
			new Command<NearbyRow>(
				row =>
				{
					if (row is not null)
					{
						row.IsExpanded = !row.IsExpanded;
					}
				});

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

	/// <summary>Shows the course of the whole line on the map (both directions when they can be told apart).</summary>
	public AsyncCommand<StopLineRow> OpenLineMapCommand { get; }

	/// <summary>Every line again: clears the line pick.</summary>
	public Command ClearLineFilterCommand { get; }

	/// <summary>Shows the cancelled rows (hidden by default; shown struck-through).</summary>
	public Command ToggleCancelledCommand { get; }

	/// <summary>Expands a nearby row's preview from one departure to three.</summary>
	public Command<NearbyRow> ExpandNearbyCommand { get; }

	public ObservableCollection<AccessRow> Accessibility { get; } = [];

	public ObservableCollection<ServicePointRow> ServicePoints { get; } = [];

	public ObservableCollection<DepartureRow> Rows { get; } = [];

	public ObservableCollection<NearbyRow> Nearby { get; } = [];

	public ObservableCollection<StopLineRow> Lines { get; } = [];

	/// <summary>The lines of the stop as filter chips above the board (see <see cref="ShowLineFilter"/>).</summary>
	public ObservableCollection<LineFilterChip> LineChips { get; } = [];

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
				OnPropertyChanged(nameof(ShowLineFilter));
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

		private set => SetProperty(ref field, value);
	}

	public bool HasLines =>
		Lines.Count > 0;

	/// <summary>Cancelled rows are hidden by default; when the toggle shows them they are struck through.</summary>
	public bool ShowCancelled
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				RebuildRows();
			}
		}
	}

	/// <summary>Whether the chip row has anything to filter: it needs the stop and its lines.</summary>
	public bool ShowLineFilter =>
		HasStop && LineChips.Count > 0;

	/// <summary>Whether any line is picked (the clear chip appears).</summary>
	public bool HasLineSelection =>
		_selectedLines.Count > 0;

	public bool ShowAccessibility
	{
		get;

		private set => SetProperty(ref field, value);
	}

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

		private set => SetProperty(ref field, value);
	}

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

	/// <summary>Where this line actually goes: the next run of the line from this stop's board, both
	/// directions when they can be told apart, as one map.</summary>
	private async Task OpenLineMapAsync(StopLineRow row)
	{
		if (Stop is not { } stop)
		{
			return;
		}

		try
		{
			var station =
				new Station
				{
					Id = stop.Id ?? string.Empty,
					ProviderId = stop.ProviderId,
					Name = stop.Name,
					Place = stop.Place,
					Latitude = stop.Latitude,
					Longitude = stop.Longitude
				};

			IReadOnlyList<LineCourse> courses =
				await _lineCourses.BothDirectionsAsync(
					new TransitLine { Name = row.Line.Name, Mode = row.Line.Mode },
					station,
					_settings.TimeoutSeconds);

			if (courses.Count == 0)
			{
				Message = _localization.CurrentStrings.Extras.LineMapNone;

				return;
			}

			LineCourse first = courses[0];

			string title =
				string.Format(
					CultureInfo.CurrentCulture,
					_localization.CurrentStrings.Extras.LineCourseTitle,
					first.LineName,
					first.FirstTerminus ?? string.Empty,
					first.LastTerminus ?? string.Empty);

			// B4: the diversions note of the line rides along (active route changes; failures stay quiet).
			string? notice = await LineCourseNotices.OfLineAsync(_network, first.LineName);

			await MapScenes.OpenAsync(
				MapScenes.FromLineCourses(courses),
				title,
				notice: notice);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Departures] line map for {row.Line.Name} failed: {ex.Message}");
		}
	}

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
				_previews?.Cancel();

				Stop = null;
				_current = [];
				Rows.Clear();
				Nearby.Clear();
				Lines.Clear();
				LineChips.Clear();
				_selectedLines.Clear();
				ShowCancelled = false;
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
				OnPropertyChanged(nameof(HasLineSelection));
				OnPropertyChanged(nameof(ShowLineFilter));

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
		_selectedLines.Clear();
		ShowCancelled = false;
		LineChips.Clear();
		OnPropertyChanged(nameof(HasLineSelection));
		OnPropertyChanged(nameof(ShowLineFilter));
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
			RebuildLineChips();

			// The board of now is what the page can still answer offline (B7); a board for a chosen
			// other time is not. The widget board passes write the same cache from their own side.
			if (IsNow)
			{
				BoardCache.Store(_providers.SelectedId, stop, IsArrival, _current);
			}

			// What is shown, not what was fetched: a filter that empties the board is honest about it.
			Message =
				Rows.Count == 0
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

			// Offline, or the provider is down: the last board of this stop still answers, stamped with
			// when it was fetched (B7). Only when nothing is on screen — a silent pass keeps the rows it
			// has, and those are at least as fresh as the cache. A cache miss is today's error path.
			if (IsNow
				&& Rows.Count == 0
				&& stop.Id is { } stopId
				&& BoardCache.Find(_providers.SelectedId, stopId, IsArrival) is { } cached)
			{
				_current = cached.Departures;
				RebuildRows();
				RebuildLineChips();

				Message =
					string.Format(
						CultureInfo.CurrentCulture,
						_localization.CurrentStrings.Departures.CachedBoard,
						Format.Time(cached.Fetched));
			}
			else if (!silent || Rows.Count == 0)
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

		// A refresh the passenger asked for also freshens the nearby previews; the 30-second pass does not.
		if (!silent && Nearby.Count > 0 && !IsDisposed)
		{
			_ = LoadNearbyPreviewsAsync();
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

			// Nearest first (the same order the "next departures around" widget uses): the preview cap
			// then reaches the stops that matter.
			foreach (NearbyStop stop in found.OrderBy(stop => stop.DistanceMeters))
			{
				Nearby.Add(new NearbyRow(stop));
			}

			OnPropertyChanged(nameof(HasNearby));
			OnPropertyChanged(nameof(ShowHint));

			if (found.Count == 0)
			{
				Message = _localization.CurrentStrings.Plan.NoStopNearby;
			}
			else
			{
				// The nearest rows preview their next departures (the widget's shared path, one row at a time).
				_ = LoadNearbyPreviewsAsync();

				if (Stop is null)
				{
					SetStop(found[0].Stop);
				}
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
			// The stop's lines are the filter chip row's full set, so they load with the stop (cached ten
			// minutes); the lines section then only shows them.
			_ = LoadLinesAsync(stop);

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
				// The star state comes with the row (the watch list is read once per load).
				Lines.Add(
					new StopLineRow(line)
					{
						IsWatched = WatchedLines.IsWatched(stop.ProviderId, line.Name)
					});
			}

			_linesLoaded = true;
			OnPropertyChanged(nameof(HasLines));
			OnPropertyChanged(nameof(LinesSummary));
			RebuildLineChips();
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
		List<DepartureRow> board =
			[.. BoardFilter.Apply(_current, _selectedLines, ShowCancelled)
				.Select(departure => new DepartureRow(departure))];

		CollectionSync.Merge(
			Rows,
			board,
			static (shown, next) => shown.Shows(next));
	}

	// ---------- The line filter and the cancelled toggle ----------

	private void ToggleLine(LineFilterChip chip)
	{
		if (!_selectedLines.Remove(chip.Key))
		{
			_selectedLines.Add(chip.Key);
		}

		ApplyLineFilter();
	}

	/// <summary>Long press on a chip: exactly this line; a long press on the only picked line clears the filter.</summary>
	private void OnlyLine(LineFilterChip chip)
	{
		if (_selectedLines.Count == 1 && _selectedLines.Contains(chip.Key))
		{
			_selectedLines.Clear();
		}
		else
		{
			_selectedLines.Clear();
			_selectedLines.Add(chip.Key);
		}

		ApplyLineFilter();
	}

	private void ClearLineFilter()
	{
		_selectedLines.Clear();

		ApplyLineFilter();
	}

	/// <summary>Carries a changed pick to the chips and the board.</summary>
	private void ApplyLineFilter()
	{
		foreach (LineFilterChip chip in LineChips)
		{
			chip.IsSelected = _selectedLines.Contains(chip.Key);
		}

		OnPropertyChanged(nameof(HasLineSelection));
		RebuildRows();
	}

	/// <summary>
	/// The chip row: every line the stop's lines say or the fetched board shows, one chip each (multi-select,
	/// see the commands). The collection is left alone when nothing changed — the row scrolls, and a rebuild
	/// would jump it back.
	/// </summary>
	private void RebuildLineChips()
	{
		IEnumerable<(string Name, TransitMode Mode)> lines =
			Lines.Select(row => (row.Line.Name, row.Line.Mode))
				.Concat(_current.Select(departure => (departure.Line.Name, departure.Line.Mode)));

		var seen = new HashSet<string>(StringComparer.Ordinal);
		List<LineFilterChip> chips = [];

		foreach ((string name, TransitMode mode) in lines.OrderBy(line => line.Name, StringComparer.CurrentCultureIgnoreCase))
		{
			if (seen.Add(BoardFilter.LineKey(name)))
			{
				chips.Add(new LineFilterChip(name, BoardFilter.LineKey(name), ToggleLine, OnlyLine));
			}
		}

		if (LineChips.Select(chip => chip.Key).SequenceEqual(chips.Select(chip => chip.Key)))
		{
			ApplyLineFilter();

			return;
		}

		LineChips.Clear();

		foreach (LineFilterChip chip in chips)
		{
			chip.IsSelected = _selectedLines.Contains(chip.Key);
			LineChips.Add(chip);
		}

		// A line without a chip (changed stop, switched provider) must not go on filtering silently.
		_selectedLines.IntersectWith((List<string>)[.. chips.Select(chip => chip.Key)]);

		OnPropertyChanged(nameof(ShowLineFilter));
		OnPropertyChanged(nameof(HasLineSelection));
		ApplyLineFilter();
	}

	// ---------- Nearby previews ----------

	/// <summary>
	/// The next departures of the nearest nearby rows, one row at a time (the nearest first): the same shared
	/// path the "next departures around" widget uses. At most eight rows per pass, cancelled with the page.
	/// </summary>
	private async Task LoadNearbyPreviewsAsync()
	{
		_previews?.Cancel();

		NearbyRow[] rows = [.. Nearby.Take(NearbyPreviewStops)];

		if (rows.Length == 0)
		{
			return;
		}

		var cts = new CancellationTokenSource();
		_previews = cts;

		try
		{
			foreach (NearbyRow row in rows)
			{
				if (cts.IsCancellationRequested || IsDisposed)
				{
					return;
				}

				IReadOnlyList<Departure> board =
					await NearbyBoards.NextOfAsync(
						_departures,
						row.Stop,
						arrival: false,
						keep: NearbyPreviewRows + NearbyBoards.Spare,
						lines: null,
						modes: ModeFilter.All,
						timeoutSeconds: _settings.TimeoutSeconds,
						cancellationToken: cts.Token);

				if (cts.IsCancellationRequested || IsDisposed)
				{
					return;
				}

				row.Adopt(
					(List<NearbyPreview>)[.. NearbyBoards.Previews(board, NearbyPreviewRows, Format.Now())
						.Select(departure => new NearbyPreview(departure))]);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Nearby previews failed: {ex.Message}");
		}
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
				OnPropertyChanged(nameof(LinesSummary));
				OnPropertyChanged(nameof(ServicePointsSummary));
				RebuildRows();
			});

	protected override void OnDisposing()
	{
		_refresh?.Cancel();
		_info?.Cancel();
		_previews?.Cancel();
	}
}
