using DDjourneys.Core.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Core.Tracking;
using DDjourneys.Controls;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One choice for the live notification: automatic, or one followed journey.</summary>
public sealed class LiveOption
{
	public required string Title { get; init; }
	public required string Subtitle { get; init; }
	public required bool IsSelected { get; init; }
	public required ICommand SelectCommand { get; init; }

	public bool HasSubtitle => Subtitle.Length > 0;
}

/// <summary>One line of the extended course view: a segment heading or a stop.</summary>
public sealed class CourseRow
{
	public required bool IsHeader { get; init; }
	public required string Text { get; init; }
	public string Time { get; init; } = string.Empty;
	public string LiveTime { get; init; } = string.Empty;
	public bool IsLate { get; init; }
	public bool IsPassed { get; init; }
	public bool IsCurrent { get; init; }
	public bool IsNext { get; init; }
	public bool IsWalk { get; init; }

	/// <summary>Platform or track of a boarding/alighting stop; empty elsewhere.</summary>
	public string Detail { get; init; } = string.Empty;

	/// <summary>Countdown to the next stop ("in 3 min"); empty for every other row.</summary>
	public string Eta { get; init; } = string.Empty;

	public bool IsStop => !IsHeader;
	public bool HasDetail => Detail.Length > 0;
	public bool IsEmphasized => IsStop && (IsNext || IsCurrent);
	public bool HasEta => Eta.Length > 0;
	public bool IsRideHeader => IsHeader && !IsWalk;
	public bool IsWalkHeader => IsHeader && IsWalk;
	public bool IsCurrentRideHeader => IsRideHeader && IsCurrent;
	public bool IsOtherRideHeader => IsRideHeader && !IsCurrent;
	public bool IsCurrentStop => IsStop && IsCurrent;
	public bool IsNextStop => IsStop && IsNext;
	public bool IsPassedStop => IsStop && IsPassed && !IsCurrent && !IsNext;
	public bool IsUpcomingStop => IsStop && !IsPassed && !IsCurrent && !IsNext;

	/// <summary>The rail is drawn in the accent colour where the journey has already been.</summary>
	public bool IsRailDone => IsPassed || IsCurrent;
	public bool IsRailAhead => !IsRailDone;
	public bool HasLiveTime => LiveTime.Length > 0;
	public bool IsOnTimeLive => HasLiveTime && !IsLate;
	public double Fade => IsPassed ? 0.55 : 1.0;
}

/// <summary>One line of the message history of a followed journey: time and text.</summary>
public sealed record HistoryRow(string Time, string Text, bool IsProblem);

/// <summary>One group of the overview: all followed journeys with the same status.</summary>
public sealed record TrackedSection(string Title, IReadOnlyList<TrackedRow> Items);

/// <summary>Display model of one followed journey. Immutable; the list is rebuilt on every change.</summary>
/// <summary>One chip of the lines of a followed journey.</summary>
public sealed record LinePart(string Text, bool IsMark, bool IsCurrent)
{
	public bool IsLine => !IsMark;
	public bool IsLineIdle => !IsMark && !IsCurrent;
}

public sealed class TrackedRow
{
	public required string PlanId { get; init; }
	public required string Title { get; init; }
	public required string Subtitle { get; init; }

	/// <summary>Times and ends for the card header (same look as the journey card).</summary>
	public string DepartureTime { get; init; } = string.Empty;

	public string ArrivalTime { get; init; } = string.Empty;

	public string DayText { get; init; } = string.Empty;

	public string FromName { get; init; } = string.Empty;

	public string? FromPlace { get; init; }

	public string ToName { get; init; } = string.Empty;

	public string? ToPlace { get; init; }

	public required string LinesText { get; init; }

	/// <summary>The rides as chips (a line, or the mark of a guaranteed change), in travel order.</summary>
	public required IReadOnlyList<LinePart> LineParts { get; init; }

	/// <summary>Opens the live map of the vehicles of this journey; null when its rides are not known.</summary>
	public required ICommand MapCommand { get; init; }

	public required bool HasMap { get; init; }
	public required string StatusText { get; init; }
	public required bool IsProblem { get; init; }
	public required bool IsCancelled { get; init; }
	public required bool IsNormalStatus { get; init; }
	public required double Progress { get; init; }
	public required bool HasProgress { get; init; }
	public required string NextText { get; init; }
	public required string NoticeText { get; init; }
	public required bool IsExpanded { get; init; }
	public required IconGlyph ExpandIcon { get; init; }
	public required bool CanPause { get; init; }
	public required string PauseText { get; init; }
	public required string StartAlertText { get; init; }
	public required bool StartAlertOn { get; init; }
	public required string LeadText { get; init; }
	public required string ChangeAlertText { get; init; }
	public required bool ChangeAlertOn { get; init; }
	public required string ProblemAlertText { get; init; }
	public required bool ProblemAlertOn { get; init; }
	public required string StopText { get; init; }
	public required string AccessibilityText { get; init; }
	public required bool IsLive { get; init; }
	public required string LiveBadgeText { get; init; }
	public required bool IsFocused { get; init; }
	public required bool IsCourseVisible { get; init; }
	public required string CourseToggleText { get; init; }
	public required IReadOnlyList<CourseRow> Course { get; init; }
	public required string CourseHint { get; init; }

	public bool HasLines => LineParts.Count > 0;
	public bool HasStatus => StatusText.Length > 0;
	public bool ShowNormalStatus => IsNormalStatus && HasStatus;
	public bool HasNext => NextText.Length > 0;
	public bool HasNotice => NoticeText.Length > 0;
	public bool NoticeIsAlert => HasNotice && (IsProblem || IsCancelled);
	public bool NoticeIsInfo => HasNotice && !IsProblem && !IsCancelled;
	public bool HasCourse => Course.Count > 0;
	public bool HasCourseHint => IsCourseVisible && Course.Count == 0;

	public required ICommand ToggleExpandedCommand { get; init; }
	public required ICommand PauseResumeCommand { get; init; }
	public required ICommand StopCommand { get; init; }
	public required ICommand ToggleStartAlertCommand { get; init; }
	public required ICommand CycleLeadCommand { get; init; }
	public required ICommand ToggleChangeAlertCommand { get; init; }
	public required ICommand ToggleProblemAlertCommand { get; init; }
	public required ICommand ToggleCourseCommand { get; init; }

	/// <summary>Hides the shown notice (swipe or the close button); a newer notice shows again.</summary>
	public required ICommand DismissNoticeCommand { get; init; }

	public required string DismissNoticeText { get; init; }

	/// <summary>Every message of the service for this journey, newest first (the reference client lists all of them).</summary>
	public required IReadOnlyList<HistoryRow> History { get; init; }

	public required string HistoryTitle { get; init; }

	public bool HasHistory => History.Count > 0;
}

/// <summary>The overview of all journeys the user follows.</summary>
public sealed partial class TrackedJourneysViewModel : DisposableViewModel, IQueryAttributable
{
	// How often fresh data is fetched depends on what is going on: a journey under way every 20 seconds, one about
	// to start every 45, otherwise every 3 minutes; after a failure sooner. Between fetches the page follows the
	// clock every 10 seconds (progress, "in progress", "arrived").
	private static readonly TimeSpan LiveRefresh = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan SoonRefresh = TimeSpan.FromSeconds(45);
	private static readonly TimeSpan IdleRefresh = TimeSpan.FromMinutes(3);
	private static readonly TimeSpan RetryRefresh = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(90);

	/// <summary>How often an open course view moves on with the clock (between server polls).</summary>
	private static readonly TimeSpan CourseTick = TimeSpan.FromSeconds(10);

	private readonly IJourneyTracker _tracker;
	private readonly LegRunResolver _runs;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;
	private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
	private readonly HashSet<string> _courses = new(StringComparer.Ordinal);

	private string? _focused;

	private CancellationTokenSource? _observation;

	public TrackedJourneysViewModel(IJourneyTracker tracker, LegRunResolver runs, AppSettings settings)
	{
		_tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
		_runs = runs ?? throw new ArgumentNullException(nameof(runs));
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_localization = LocalizationService.Current;

		ListenToLocalization(_localization, OnLocalizationChanged);

		RefreshCommand = new AsyncCommand(RefreshAsync, null, ShowError);
		DeleteAllCommand = new AsyncCommand(DeleteAllAsync, () => HasItems, ShowError);

		Rebuild();
	}

	/// <summary>Asks the user to confirm (title, message); set by the page.</summary>
	public Func<string, string, Task<bool>>? Confirm { get; set; }

	public AsyncCommand RefreshCommand { get; }

	public AsyncCommand DeleteAllCommand { get; }

	public IReadOnlyList<TrackedSection> Sections
	{
		get;
		private set => SetProperty(ref field, value);
	} = [];

	public bool HasItems
	{
		get;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(IsEmpty));
				DeleteAllCommand.RaiseCanExecuteChanged();
			}
		}
	}

	public bool IsEmpty => !HasItems;

	public bool IsRefreshing
	{
		get;
		set => SetProperty(ref field, value);
	}

	public string? ErrorText
	{
		get;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasError));
			}
		}
	}

	public bool HasError => ErrorText is not null;

	public bool NotificationsBlocked
	{
		get;
		private set => SetProperty(ref field, value);
	}

	/// <summary>Choices for the live notification; empty where the platform has none.</summary>
	public IReadOnlyList<LiveOption> LiveOptions
	{
		get;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasLiveOptions));
			}
		}
	} = [];

	public bool HasLiveOptions => LiveOptions.Count > 0;

	/// <summary>Raised with a plan id after <see cref="Focus"/>, once the rows exist; the page scrolls to it.</summary>
	public event EventHandler<string>? FocusRequested;

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.FocusPlan, out object? value))
		{
			Focus(value as string);
		}
	}

	/// <summary>Opens one followed journey with its course and brings it into view.</summary>
	public void Focus(string? planId)
	{
		if (string.IsNullOrWhiteSpace(planId))
		{
			return;
		}

		_focused = planId;
		_expanded.Add(planId);
		_courses.Add(planId);

		Rebuild();

		FocusRequested?.Invoke(this, planId);
	}

	// ----- Lifetime of the page -----

	public void StartObserving()
	{
		if (_observation is not null)
		{
			return;
		}

		_observation = new CancellationTokenSource();

		_tracker.WatchedChanged += OnWatchedChanged;

		Rebuild();

		_ = AutoRefreshAsync(_observation.Token);
		_ = TickCoursesAsync(_observation.Token);
	}

	/// <summary>Open course views follow the clock; the server data itself refreshes with the tracker.</summary>
	private async Task TickCoursesAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(CourseTick, cancellationToken);

				if (_courses.Count > 0 || NeedsClock())
				{
					RebuildIfAlive();
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	protected override void OnDisposing() =>
		StopObserving();

	public void StopObserving()
	{
		_tracker.WatchedChanged -= OnWatchedChanged;

		_observation?.Cancel();
		_observation?.Dispose();
		_observation = null;
	}

	private async Task AutoRefreshAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await RefreshCoreAsync(cancellationToken);

				NotificationsBlocked = !await _tracker.CanNotifyAsync(cancellationToken);

				await Task.Delay(NextRefreshDelay(), cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>How long until the next fetch: short while something is under way or about to start.</summary>
	private TimeSpan NextRefreshDelay()
	{
		if (ErrorText is not null)
		{
			return RetryRefresh;
		}

		DateTimeOffset now = DateTimeOffset.UtcNow;
		TimeSpan delay = IdleRefresh;

		foreach (WatchedJourney journey in _tracker.Watched)
		{
			WatchedJourney current = Reconcile(journey, now);

			if (current.Status == WatchStatus.Deactivated || current.Status == WatchStatus.Recent)
			{
				continue;
			}

			if (current.Phase is TrackingPhase.InProgress or TrackingPhase.AtInterchange or TrackingPhase.AtRisk)
			{
				return LiveRefresh;
			}

			if (current.Departure is { } departure && departure - now < SoonWindow)
			{
				delay = SoonRefresh < delay ? SoonRefresh : delay;
			}
		}

		return delay;
	}

	/// <summary>Something is under way or starts soon: the rows have to follow the clock.</summary>
	private bool NeedsClock()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;

		return _tracker.Watched.Any(
			journey =>
			{
				WatchedJourney current = Reconcile(journey, now);

				return current.Status is WatchStatus.Active or WatchStatus.Planned
					&& (current.Phase != TrackingPhase.Planned
						|| (current.Departure is { } departure && departure - now < SoonWindow));
			});
	}

	/// <summary>
	/// What the clock says about a journey since the last answer of the service: a planned one whose time has come
	/// is under way, one whose arrival has passed has arrived, and the progress moves on with the time. The next
	/// answer of the service replaces all of it.
	/// </summary>
	private WatchedJourney Reconcile(WatchedJourney journey, DateTimeOffset now)
	{
		if (journey.Status is WatchStatus.Deactivated or WatchStatus.Recent
			|| journey.Phase is TrackingPhase.Cancelled or TrackingPhase.Paused or TrackingPhase.Arrived
			|| journey.Departure is not { } departure
			|| journey.Arrival is not { } arrival
			|| arrival <= departure)
		{
			return journey;
		}

		if (now >= arrival)
		{
			return journey with
			{
				Status = WatchStatus.Recent,
				Phase = TrackingPhase.Arrived,
				Progress = 1
			};
		}

		if (now < departure)
		{
			return journey;
		}

		double elapsed =
			_progressBase.TryGetValue(journey.PlanId, out (double Progress, DateTimeOffset At) known)
				? (now - known.At) / (arrival - departure)
				: 0;

		double basis =
			known.At == default
				? Math.Max(journey.Progress, (now - departure) / (arrival - departure))
				: known.Progress;

		return journey with
		{
			Status = WatchStatus.Active,
			Phase = journey.Phase == TrackingPhase.Planned ? TrackingPhase.InProgress : journey.Phase,
			Progress = Math.Clamp(basis + elapsed, 0, 0.99)
		};
	}

	/// <summary>The progress each journey had when it last changed, and when: the base for moving on with the clock.</summary>
	private readonly Dictionary<string, (double Progress, DateTimeOffset At)> _progressBase = [];

	private void NoteProgress()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;

		foreach (WatchedJourney journey in _tracker.Watched)
		{
			if (!_progressBase.TryGetValue(journey.PlanId, out (double Progress, DateTimeOffset At) known)
				|| Math.Abs(known.Progress - journey.Progress) > 0.0005)
			{
				_progressBase[journey.PlanId] = (journey.Progress, now);
			}
		}
	}

	private async Task RefreshAsync()
	{
		try
		{
			await RefreshCoreAsync(CancellationToken.None);
		}
		finally
		{
			IsRefreshing = false;
		}
	}

	private async Task RefreshCoreAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _tracker.RefreshAsync(cancellationToken);

			ErrorText = null;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	// ----- Commands -----

	private async Task DeleteAllAsync()
	{
		TrackingStrings strings = _localization.CurrentStrings.Tracking;

		if (Confirm is not null && !await Confirm(strings.DeleteAll, strings.DeleteAllMessage))
		{
			return;
		}

		await _tracker.DeleteAllAsync();

		_expanded.Clear();
		_courses.Clear();
	}

	private async Task StopAsync(WatchedJourney journey)
	{
		TrackingStrings strings = _localization.CurrentStrings.Tracking;

		if (Confirm is not null && !await Confirm(strings.DeleteTitle, strings.DeleteMessage))
		{
			return;
		}

		_expanded.Remove(journey.PlanId);
		_courses.Remove(journey.PlanId);

		await _tracker.DeleteAsync(journey.PlanId);
	}

	private Task PauseResumeAsync(WatchedJourney journey) =>
		_tracker.SetActiveAsync(journey.PlanId, journey.Status == WatchStatus.Deactivated);

	private Task ChangeOptionsAsync(WatchedJourney journey, Func<WatchOptions, WatchOptions> change) =>
		_tracker.SetOptionsAsync(journey.PlanId, change(journey.Options));

	private void ToggleCourse(WatchedJourney journey)
	{
		if (!_courses.Remove(journey.PlanId))
		{
			_courses.Add(journey.PlanId);
		}

		Rebuild();
	}

	private Task SelectLiveAsync(string? planId) =>
		_tracker.SetPreferredLivePlanAsync(planId);

	private void ToggleExpanded(WatchedJourney journey)
	{
		if (!_expanded.Remove(journey.PlanId))
		{
			_expanded.Add(journey.PlanId);
		}

		Rebuild();
	}

	private void ShowError(Exception ex) => ErrorText = ex.Message;

	// ----- Display -----

	private void OnWatchedChanged(object? sender, EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				NoteProgress();
				RebuildIfAlive();
			});

	private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(RebuildIfAlive);

	private void RebuildIfAlive()
	{
		if (!IsDisposed)
		{
			Rebuild();
		}
	}

	private void Rebuild()
	{
		try
		{
			NoteProgress();

			DateTimeOffset now = DateTimeOffset.UtcNow;

			List<WatchedJourney> journeys = [.. _tracker.Watched.Select(item => Reconcile(item, now))];

			TrackingStrings strings = _localization.CurrentStrings.Tracking;

			var sections = new List<TrackedSection>();

			foreach ((WatchStatus status, string title) in new[]
			{
				(WatchStatus.Active, strings.SectionActive),
				(WatchStatus.Planned, strings.SectionPlanned),
				(WatchStatus.Deactivated, strings.SectionPaused),
				(WatchStatus.Recent, strings.SectionRecent)
			})
			{
				// One journey that cannot be shown must not hide the others (nor the whole page, as an empty list would).
				var rows = new List<TrackedRow>();

				foreach (WatchedJourney item in journeys.Where(item => item.Status == status))
				{
					try
					{
						rows.Add(CreateRow(item, strings));
					}
					catch (Exception ex)
					{
						DiagnosticLog.Write($"Followed journey {item.PlanId} not shown: {ex}");
					}
				}

				if (rows.Count > 0)
				{
					sections.Add(new TrackedSection(title, rows));
				}
			}

			Sections = sections;
			HasItems = sections.Count > 0;

			DiagnosticLog.Write($"[Followed] {journeys.Count} journey(s) from the tracker, {sections.Count} section(s) shown");

			try
			{
				LiveOptions = CreateLiveOptions(journeys, strings);
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write($"Live options not built: {ex.Message}");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Followed journeys display failed:\n{ex}");
		}
	}

	/// <summary>The same as chips: a line each, the mark of a guaranteed change between; the line under way is highlighted.</summary>
	private static List<LinePart> LinePartsOf(WatchedJourney journey, TrackingStrings strings)
	{
		string[] lines = [.. journey.Lines.Where(item => !string.IsNullOrWhiteSpace(item))];

		bool marks =
			journey.EnsuredChanges is { } ensured
			&& ensured.Count == lines.Length - 1;

		bool underWay = journey.Phase == TrackingPhase.InProgress;
		bool found = false;
		var parts = new List<LinePart>(lines.Length * 2);

		for (int i = 0; i < lines.Length; i++)
		{
			bool current = underWay && !found && lines[i] == journey.CurrentLine;

			found |= current;

			parts.Add(new LinePart(lines[i], false, current));

			if (marks && journey.EnsuredChanges![i])
			{
				parts.Add(new LinePart(strings.GuaranteedChange, true, false));
			}
		}

		return parts;
	}

	/// <summary>Looks up the vehicles of the rides still to come and opens the live page following all of them.</summary>
	private async Task OpenMapAsync(WatchedJourney journey)
	{
		IReadOnlyList<FollowedRide> rides = FollowedRides.Load(journey.PlanId);

		DateTimeOffset limit = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(3);

		// Rides that are over have no vehicle to look for; when all are, show them anyway.
		List<FollowedRide> pending =
			[.. rides.Where(ride => ride.Arrival is null || ride.Arrival > limit)];

		var targets = new List<TrackTarget>();

		foreach (FollowedRide ride in pending.Count > 0 ? pending : rides)
		{
			try
			{
				if (await _runs.ResolveAsync(ride.ToLeg(), ride.Line, _settings.TimeoutSeconds) is { } target)
				{
					targets.Add(target);
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				DiagnosticLog.Write($"Looking up the run of line {ride.Line} failed: {ex.Message}");
			}
		}

		if (targets.Count == 0)
		{
			ErrorText = _localization.CurrentStrings.Extras.TrackNoCourse;

			return;
		}

		ErrorText = null;

		await Shell.Current.GoToAsync(
			Routes.Vehicles,
			new ShellNavigationQueryParameters
			{
				[Routes.TrackSet] = (IReadOnlyList<TrackTarget>)targets
			});
	}

	/// <summary>"7 · gesicherter Anschluss · 6": the lines, with a guaranteed change named where it happens.</summary>
	private static string LinesOf(WatchedJourney journey, TrackingStrings strings)
	{
		string[] lines = [.. journey.Lines.Where(item => !string.IsNullOrWhiteSpace(item))];

		// The marks only line up when every ride has its own entry (equal names in a row were merged).
		if (journey.EnsuredChanges is not { } ensured || ensured.Count != lines.Length - 1)
		{
			return string.Join(" · ", lines);
		}

		var parts = new List<string>(lines.Length * 2);

		for (int i = 0; i < lines.Length; i++)
		{
			parts.Add(lines[i]);

			if (i < ensured.Count && ensured[i])
			{
				parts.Add(strings.GuaranteedChange);
			}
		}

		return string.Join(" · ", parts);
	}

	private TrackedRow CreateRow(WatchedJourney journey, TrackingStrings strings)
	{
		CultureInfo culture = CultureInfo.CurrentCulture;
		WatchOptions options = journey.Options;
		bool expanded = _expanded.Contains(journey.PlanId);

		string times = $"{Format.TimeOrDash(journey.Departure)}–{Format.TimeOrDash(journey.Arrival)}";

		string subtitle =
			journey.Departure is { } departure
				? $"{Format.DayLabel(Format.ToWall(departure).Date)}, {times}"
				: times;

		if (journey.IsPeriodic)
		{
			subtitle += $" · {strings.Periodic}";
		}

		string status =
			journey.Status switch
			{
				// The departure time is in the subtitle already: nothing new to say about a journey that has not started.
				WatchStatus.Planned when journey.Phase == TrackingPhase.Planned && journey.Departure is not null =>
					string.Empty,

				WatchStatus.Recent when journey.Phase == TrackingPhase.Arrived =>
					string.Format(culture, strings.ArrivedAt, Format.TimeOrDash(journey.Arrival)),

				_ => PhaseText(journey.Phase, strings)
			};

		// (The line under way is marked in the chips, not repeated here.)
		string next =
			journey.NextStop is { Length: > 0 } stop
				? string.Format(culture, strings.NextStop, $"{stop} {Format.TimeOrDash(journey.NextStopTime)}".Trim())
				: string.Empty;

		bool problem = journey.Phase == TrackingPhase.AtRisk;
		bool cancelled = journey.Phase == TrackingPhase.Cancelled;

		string route = $"{journey.Origin} → {journey.Destination}";

		bool live = _tracker.LivePlanId == journey.PlanId;
		bool course = _courses.Contains(journey.PlanId);
		IReadOnlyList<CourseRow> rows = course ? CreateCourse(journey.PlanId, strings) : [];

		return new TrackedRow
		{
			PlanId = journey.PlanId,
			Title = route,
			Subtitle = subtitle,
			DepartureTime = Format.TimeOrDash(journey.Departure),
			ArrivalTime = Format.TimeOrDash(journey.Arrival),
			DayText =
				(journey.Departure is { } day ? Format.DayLabel(Format.ToWall(day).Date) : string.Empty)
				+ (journey.IsPeriodic ? $" · {strings.Periodic}" : string.Empty),
			FromName = StopLabel.NameFor(journey.Origin, null),
			FromPlace = StopLabel.PlaceFor(journey.Origin, null),
			ToName = StopLabel.NameFor(journey.Destination, null),
			ToPlace = StopLabel.PlaceFor(journey.Destination, null),
			LinesText = LinesOf(journey, strings),
			LineParts = LinePartsOf(journey, strings),
			HasMap = FollowedRides.Has(journey.PlanId),
			MapCommand = new AsyncCommand(() => OpenMapAsync(journey), null, ShowError),
			StatusText = status,
			IsProblem = problem,
			IsCancelled = cancelled,
			IsNormalStatus = !problem && !cancelled,
			Progress = Math.Clamp(journey.Progress, 0, 1),
			HasProgress = journey.Status == WatchStatus.Active && journey.Phase is
				TrackingPhase.InProgress or TrackingPhase.AtInterchange or TrackingPhase.AtRisk,
			NextText = next,
			NoticeText = journey.LatestNotice ?? string.Empty,
			IsExpanded = expanded,
			ExpandIcon = expanded ? IconGlyph.ChevronUp : IconGlyph.ChevronDown,
			CanPause = journey.Status != WatchStatus.Recent,
			PauseText = journey.Status == WatchStatus.Deactivated ? strings.Resume : strings.Pause,
			StartAlertText = strings.AlertStart,
			StartAlertOn = options.StartAlert,
			LeadText = string.Format(culture, strings.LeadMinutes, options.StartLeadMinutes),
			ChangeAlertText = strings.AlertChange,
			ChangeAlertOn = options.ChangeAlert,
			ProblemAlertText = strings.AlertProblem,
			ProblemAlertOn = options.ProblemAlert,
			StopText = strings.StopFollowing,
			AccessibilityText = live ? $"{route}, {subtitle}, {status}, {strings.LiveBadge}" : $"{route}, {subtitle}, {status}",
			IsLive = live,
			LiveBadgeText = strings.LiveBadge,
			IsFocused = _focused == journey.PlanId,
			IsCourseVisible = course,
			CourseToggleText = course ? strings.CourseHide : strings.CourseShow,
			Course = rows,
			CourseHint = strings.CourseNotYet,
			DismissNoticeText = strings.DismissNotice,
			HistoryTitle = strings.NoticeHistory,
			History =
				(List<HistoryRow>)
				[.. (journey.Notices ?? [])
					.Select(item => new HistoryRow(Format.TimeOrDash(item.Time), item.Text, item.IsProblem))],
			DismissNoticeCommand = new AsyncCommand(() => _tracker.DismissNoticeAsync(journey.PlanId), null, ShowError),
			ToggleCourseCommand = new Command(() => ToggleCourse(journey)),
			ToggleExpandedCommand = new Command(() => ToggleExpanded(journey)),
			PauseResumeCommand = new AsyncCommand(() => PauseResumeAsync(journey), null, ShowError),
			StopCommand = new AsyncCommand(() => StopAsync(journey), null, ShowError),
			ToggleStartAlertCommand =
				new AsyncCommand(
					() => ChangeOptionsAsync(journey, o => o with { StartAlert = !o.StartAlert }),
					null,
					ShowError),
			CycleLeadCommand =
				new AsyncCommand(
					() => ChangeOptionsAsync(journey, o => o with { StartLeadMinutes = NextLead(o.StartLeadMinutes) }),
					null,
					ShowError),
			ToggleChangeAlertCommand =
				new AsyncCommand(
					() => ChangeOptionsAsync(journey, o => o with { ChangeAlert = !o.ChangeAlert }),
					null,
					ShowError),
			ToggleProblemAlertCommand =
				new AsyncCommand(
					() => ChangeOptionsAsync(journey, o => o with { ProblemAlert = !o.ProblemAlert }),
					null,
					ShowError)
		};
	}

	/// <summary>"Automatic" plus every journey that can still be live (not completed, not paused).</summary>
	private List<LiveOption> CreateLiveOptions(
		IReadOnlyList<WatchedJourney> journeys,
		TrackingStrings strings)
	{
		if (!_tracker.HasLiveSurface)
		{
			return [];
		}

		List<WatchedJourney> candidates =
			[.. journeys.Where(item => item.Status is WatchStatus.Active or WatchStatus.Planned)];

		if (candidates.Count < 2 && _tracker.PreferredLivePlanId is null)
		{
			// Nothing to choose between.
			return [];
		}

		string? preferred = _tracker.PreferredLivePlanId;

		var options =
			new List<LiveOption>(candidates.Count + 1)
			{
				new()
				{
					Title = strings.LiveAutomatic,
					Subtitle = strings.LiveAutomaticHint,
					IsSelected = preferred is null,
					SelectCommand = new AsyncCommand(() => SelectLiveAsync(null), null, ShowError)
				}
			};

		foreach (WatchedJourney journey in candidates)
		{
			string planId = journey.PlanId;

			options.Add(
				new LiveOption
				{
					Title = $"{journey.Origin} → {journey.Destination}",
					Subtitle =
						journey.Departure is { } departure
							? $"{Format.DayLabel(Format.ToWall(departure).Date)}, {Format.TimeOrDash(departure)}"
							: string.Empty,
					IsSelected = preferred == planId,
					SelectCommand = new AsyncCommand(() => SelectLiveAsync(planId), null, ShowError)
				});
		}

		return options;
	}

	/// <summary>The course as flat rows: a heading per ride or walk, then its stops.</summary>
	private List<CourseRow> CreateCourse(string planId, TrackingStrings strings)
	{
		if (_tracker.GetTrip(planId) is not { IsEmpty: false } trip)
		{
			return [];
		}

		var rows = new List<CourseRow>();

		foreach (TrackedSegment segment in trip.Segments)
		{
			if (segment.IsWalk)
			{
				// A change within the same stop and platform needs no walk row.
				if (segment.IsInPlaceChange)
				{
					continue;
				}

				rows.Add(
					new CourseRow
					{
						IsHeader = true,
						IsWalk = true,
						IsPassed = segment.IsPassed,
						IsCurrent = segment.IsCurrent,
						Text = WalkText(segment, strings)
					});

				continue;
			}

			rows.Add(
				new CourseRow
				{
					IsHeader = true,
					IsPassed = segment.IsPassed,
					IsCurrent = segment.IsCurrent,
					Text =
						segment.Direction is { Length: > 0 } direction
							? $"{segment.Line} \u2192 {direction}"
							: segment.Line ?? string.Empty
				});

			for (int i = 0; i < segment.Stops.Count; i++)
			{
				TrackedStop stop = segment.Stops[i];
				TimeSpan delay = stop.Delay ?? TimeSpan.Zero;
				bool boardsOrAlights = i == 0 || i == segment.Stops.Count - 1;

				rows.Add(
					new CourseRow
					{
						IsHeader = false,
						Text = stop.Name,
						Time = Format.TimeOrDash(stop.Scheduled ?? stop.Realtime),
						LiveTime =
							stop.Realtime is { } realtime && stop.Scheduled is not null
								? Format.TimeOrDash(realtime)
								: string.Empty,
						IsLate = delay >= TimeSpan.FromMinutes(1),
						IsPassed = stop.State == TrackedStopState.Passed,
						IsCurrent = stop.State == TrackedStopState.Current,
						IsNext = stop.State == TrackedStopState.Next,
						Detail = boardsOrAlights ? PlatformText(stop, strings) : string.Empty,
						Eta = stop.State == TrackedStopState.Next ? EtaText(stop, trip.At, strings) : string.Empty
					});
			}
		}

		return rows;
	}

	private static string PlatformText(TrackedStop stop, TrackingStrings strings) =>
		string.IsNullOrWhiteSpace(stop.Platform)
			? string.Empty
			: string.Format(
				CultureInfo.CurrentCulture,
				stop.PlatformIsTrack ? strings.CourseTrack : strings.CoursePlatform,
				stop.Platform);

	/// <summary>"Walk to Hauptbahnhof, Platform 3 · 4 min".</summary>
	private static string WalkText(TrackedSegment segment, TrackingStrings strings)
	{
		CultureInfo culture = CultureInfo.CurrentCulture;
		string text = string.Format(culture, strings.CourseWalk, segment.To?.Name ?? string.Empty);

		if (segment.To is { } to && PlatformText(to, strings) is { Length: > 0 } platform)
		{
			text = $"{text}, {platform}";
		}

		if (segment.Duration is { } duration && duration >= TimeSpan.FromMinutes(1))
		{
			text = $"{text} \u00b7 {string.Format(culture, strings.CourseWalkMinutes, (int)Math.Round(duration.TotalMinutes))}";
		}

		return text;
	}

	private static string EtaText(TrackedStop stop, DateTimeOffset now, TrackingStrings strings)
	{
		if (stop.Effective is not { } time)
		{
			return string.Empty;
		}

		int minutes = (int)Math.Ceiling((time - now).TotalMinutes);

		return minutes <= 0
			? strings.CourseNow
			: string.Format(CultureInfo.CurrentCulture, strings.CourseIn, minutes);
	}

	private static int NextLead(int current)
	{
		IReadOnlyList<int> choices = WatchOptions.LeadChoices;

		foreach (int choice in choices)
		{
			if (choice > current)
			{
				return choice;
			}
		}

		return choices[0];
	}

	internal static string PhaseText(TrackingPhase phase, TrackingStrings strings) =>
		phase switch
		{
			TrackingPhase.InProgress => strings.PhaseInProgress,
			TrackingPhase.AtInterchange => strings.PhaseAtInterchange,
			TrackingPhase.AtRisk => strings.PhaseAtRisk,
			TrackingPhase.Cancelled => strings.PhaseCancelled,
			TrackingPhase.Arrived => strings.PhaseArrived,
			TrackingPhase.Paused => strings.PhasePaused,
			_ => strings.PhasePlanned
		};
}
