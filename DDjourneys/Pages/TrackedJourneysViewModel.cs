using DDjourneys.Core.Diagnostics;
using System.Globalization;
using System.Windows.Input;
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

/// <summary>One group of the overview: all followed journeys with the same status.</summary>
public sealed record TrackedSection(string Title, IReadOnlyList<TrackedRow> Items);

/// <summary>Display model of one followed journey. Immutable; the list is rebuilt on every change.</summary>
public sealed class TrackedRow
{
	public required string PlanId { get; init; }
	public required string Title { get; init; }
	public required string Subtitle { get; init; }
	public required string LinesText { get; init; }
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

	public bool HasLines => LinesText.Length > 0;
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
}

/// <summary>The overview of all journeys the user follows.</summary>
public sealed class TrackedJourneysViewModel : DisposableViewModel, IQueryAttributable
{
	private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);

	/// <summary>How often an open course view moves on with the clock (between server polls).</summary>
	private static readonly TimeSpan CourseTick = TimeSpan.FromSeconds(15);

	private readonly IJourneyTracker _tracker;
	private readonly LocalizationService _localization;
	private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
	private readonly HashSet<string> _courses = new(StringComparer.Ordinal);

	private string? _focused;

	private CancellationTokenSource? _observation;

	public TrackedJourneysViewModel(IJourneyTracker tracker)
	{
		_tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
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
		get => field;
		private set => SetProperty(ref field, value);
	} = [];

	public bool HasItems
	{
		get => field;
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
		get => field;
		set => SetProperty(ref field, value);
	}

	public string? ErrorText
	{
		get => field;
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
		get => field;
		private set => SetProperty(ref field, value);
	}

	/// <summary>Choices for the live notification; empty where the platform has none.</summary>
	public IReadOnlyList<LiveOption> LiveOptions
	{
		get => field;
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

				if (_courses.Count > 0)
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

				await Task.Delay(AutoRefreshInterval, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
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
		MainThread.BeginInvokeOnMainThread(RebuildIfAlive);

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
			IReadOnlyList<WatchedJourney> journeys = _tracker.Watched;
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
				List<TrackedRow> rows =
					[.. journeys.Where(item => item.Status == status).Select(item => CreateRow(item, strings))];

				if (rows.Count > 0)
				{
					sections.Add(new TrackedSection(title, rows));
				}
			}

			Sections = sections;
			HasItems = journeys.Count > 0;
			LiveOptions = CreateLiveOptions(journeys, strings);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Followed journeys display failed:\n{ex}");
		}
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
				WatchStatus.Planned when journey.Phase == TrackingPhase.Planned && journey.Departure is not null =>
					string.Format(culture, strings.Departs, Format.TimeOrDash(journey.Departure)),

				WatchStatus.Recent when journey.Phase == TrackingPhase.Arrived =>
					string.Format(culture, strings.ArrivedAt, Format.TimeOrDash(journey.Arrival)),

				_ => PhaseText(journey.Phase, strings)
			};

		if (journey.CurrentLine is { Length: > 0 } line && journey.Phase == TrackingPhase.InProgress)
		{
			status = $"{status} · {line}";
		}

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
			LinesText = LinesOf(journey, strings),
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
	private IReadOnlyList<LiveOption> CreateLiveOptions(
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
	private IReadOnlyList<CourseRow> CreateCourse(string planId, TrackingStrings strings)
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
