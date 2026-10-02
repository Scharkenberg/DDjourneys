using System.Globalization;
using System.Windows.Input;
using DDjourneys.Core.Tracking;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

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
	public required string ExpandGlyph { get; init; }
	public required bool CanPause { get; init; }
	public required string PauseText { get; init; }
	public required string StartAlertText { get; init; }
	public required string LeadText { get; init; }
	public required string ChangeAlertText { get; init; }
	public required string ProblemAlertText { get; init; }
	public required string StopText { get; init; }
	public required string AccessibilityText { get; init; }

	public bool HasLines => LinesText.Length > 0;
	public bool HasNext => NextText.Length > 0;
	public bool HasNotice => NoticeText.Length > 0;

	public required ICommand ToggleExpandedCommand { get; init; }
	public required ICommand PauseResumeCommand { get; init; }
	public required ICommand StopCommand { get; init; }
	public required ICommand ToggleStartAlertCommand { get; init; }
	public required ICommand CycleLeadCommand { get; init; }
	public required ICommand ToggleChangeAlertCommand { get; init; }
	public required ICommand ToggleProblemAlertCommand { get; init; }
}

/// <summary>The overview of all journeys the user follows.</summary>
public sealed class TrackedJourneysViewModel : ObservableObject
{
	private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);

	private readonly IJourneyTracker _tracker;
	private readonly LocalizationService _localization;
	private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

	private CancellationTokenSource? _observation;

	public TrackedJourneysViewModel(IJourneyTracker tracker)
	{
		_tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
		_localization = LocalizationService.Current;

		_localization.PropertyChanged += OnLocalizationChanged;

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
	}

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
	}

	private async Task StopAsync(WatchedJourney journey)
	{
		TrackingStrings strings = _localization.CurrentStrings.Tracking;

		if (Confirm is not null && !await Confirm(strings.DeleteTitle, strings.DeleteMessage))
		{
			return;
		}

		_expanded.Remove(journey.PlanId);

		await _tracker.DeleteAsync(journey.PlanId);
	}

	private Task PauseResumeAsync(WatchedJourney journey) =>
		_tracker.SetActiveAsync(journey.PlanId, journey.Status == WatchStatus.Deactivated);

	private Task ChangeOptionsAsync(WatchedJourney journey, Func<WatchOptions, WatchOptions> change) =>
		_tracker.SetOptionsAsync(journey.PlanId, change(journey.Options));

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
		MainThread.BeginInvokeOnMainThread(Rebuild);

	private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(Rebuild);

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
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Followed journeys display failed:\n{ex}");
		}
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

		static string Mark(bool on, string text) => on ? $"✓ {text}" : text;

		return new TrackedRow
		{
			PlanId = journey.PlanId,
			Title = route,
			Subtitle = subtitle,
			LinesText = string.Join(" · ", journey.Lines.Where(item => !string.IsNullOrWhiteSpace(item))),
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
			ExpandGlyph = expanded ? "▴" : "▾",
			CanPause = journey.Status != WatchStatus.Recent,
			PauseText = journey.Status == WatchStatus.Deactivated ? strings.Resume : strings.Pause,
			StartAlertText = Mark(options.StartAlert, strings.AlertStart),
			LeadText = string.Format(culture, strings.LeadMinutes, options.StartLeadMinutes),
			ChangeAlertText = Mark(options.ChangeAlert, strings.AlertChange),
			ProblemAlertText = Mark(options.ProblemAlert, strings.AlertProblem),
			StopText = strings.StopFollowing,
			AccessibilityText = $"{route}, {subtitle}, {status}",
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
