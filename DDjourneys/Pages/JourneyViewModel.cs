using DDjourneys.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Contract;
using DDjourneys.Controls;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Services;
using DDjourneys.Core.Tracking;
using DDjourneys.Localization;
using DDjourneys.Support;
using DDjourneys.Support.Sharing;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DDjourneys.Pages;

public sealed partial class JourneyViewModel :
	DisposableViewModel,
	IQueryAttributable
{
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;
	private readonly IJourneyTracker _tracker;
	private readonly ContractSession _contract;
	private CancellationTokenSource? _trackingObservation;

	private Journey? _journey;
	private JourneyQuery? _query;
	private readonly JourneyService _journeys;
	private CancellationTokenSource? _alternative;

	/// <summary>The journey as searched, once a leg alternative replaced it: following the alternative replaces a followed original.</summary>
	private Journey? _alternativeOf;


	private readonly ProviderRegistry _providers;

	private readonly LegRunResolver _runs;

	private readonly FareBorrower _fareBorrower;

	/// <summary>Tickets borrowed from another interface for the journey shown (a provider that quotes none).</summary>
	private (Journey Journey, IReadOnlyList<JourneyFare> Fares)? _borrowedFares;

	private CancellationTokenSource? _borrowing;


	/// <summary>Saves (and removes) the connection this journey belongs to; empty when it was opened without a search.</summary>
	public RouteBookmark Bookmark { get; }

	public JourneyViewModel(
		AppSettings settings,
		IJourneyTracker tracker,
		ProviderRegistry providers,
		ContractSession contract,
		JourneyService journeys,
		PlaceStore places,
		LegRunResolver runs,
		FareBorrower fareBorrower)
	{
		ArgumentNullException.ThrowIfNull(places);

		_runs = runs ?? throw new ArgumentNullException(nameof(runs));
		_fareBorrower = fareBorrower ?? throw new ArgumentNullException(nameof(fareBorrower));

		Bookmark =
			new RouteBookmark(
				places,
				() => _query is { } asked
					? (asked.From, asked.To)
					: null,
				hideWhenUnavailable: true);

		_journeys = journeys ?? throw new ArgumentNullException(nameof(journeys));

		_providers = providers ?? throw new ArgumentNullException(nameof(providers));

		ArgumentNullException.ThrowIfNull(
			settings);

		_settings =
			settings;
		_tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
		_contract = contract ?? throw new ArgumentNullException(nameof(contract));

		_localization =
			LocalizationService.Current;


		ListenToLocalization(
			_localization,
			OnLocalizationChanged);


		ToggleStopsCommand =
			new Command<LegRow>(
				ToggleStops);


		ToggleFaresCommand =
			new Command(
				() => FaresExpanded = !FaresExpanded);

		OpenFareCommand =
			new AsyncCommand<FareRow>(
				row => row.Link is { } link
					? Launcher.Default.OpenAsync(link)
					: Task.CompletedTask);


		ShareCommand =
			new AsyncCommand(
				ShareAsync);
		FollowJourneyCommand = new AsyncCommand(FollowJourneyAsync, () => _journey is not null && CanFollow, ShowTrackingError);
		PauseCommand = new AsyncCommand(() => SetPausedAsync(true), () => CanPause, ShowTrackingError);
		ResumeCommand = new AsyncCommand(() => SetPausedAsync(false), () => IsPaused, ShowTrackingError);
		StopFollowingCommand = new AsyncCommand(StopFollowingAsync, () => IsFollowed, ShowTrackingError);
		OpenFollowedCommand = new AsyncCommand(OpenFollowedAsync, null, ShowTrackingError);
		OpenExpertCommand = new AsyncCommand(OpenExpertAsync);
		HandOffCommand = new AsyncCommand(HandOffAsync);

		LegEarlierCommand =
			new AsyncCommand<LegRow>(
				row => ShowAlternativeAsync(row, previous: true));

		LegLaterCommand =
			new AsyncCommand<LegRow>(
				row => ShowAlternativeAsync(row, previous: false));

		OpenDocumentCommand =
			new AsyncCommand(
				OpenDocumentAsync);

		ShowLiveCommand =
			new AsyncCommand<LegRow>(
				ShowLiveAsync);

		OpenMapCommand =
			new AsyncCommand(
				OpenMapAsync);

		RefreshCommand = new AsyncCommand(RefreshAsync);

		Actions =
			new JourneyActions
			{
				PdfCommand = OpenDocumentCommand,
				HandOffCommand = HandOffCommand,
				FollowCommand = new AsyncCommand(ToggleFollowAsync, null, ShowTrackingError),
				PauseCommand = new AsyncCommand(TogglePauseAsync, null, ShowTrackingError),
				NoticesCommand = new Command(() => ScrollToNotices?.Invoke())
			};

		Subscribe(
			() => _contract.Changed += OnContractChanged,
			() => _contract.Changed -= OnContractChanged);
	}


	/// <summary>
	/// Set when the journey could not be displayed;
	/// the page shows it instead of crashing.
	/// </summary>
	public string? LoadError
	{
		get;

		private set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(HasError));
			}
		}
	}


	public bool HasError =>
		LoadError is not null;


	public Command<LegRow> ToggleStopsCommand { get; }

	public AsyncCommand ShareCommand { get; }

	public AsyncCommand FollowJourneyCommand { get; }

	public AsyncCommand PauseCommand { get; }

	public AsyncCommand ResumeCommand { get; }

	public AsyncCommand StopFollowingCommand { get; }

	public AsyncCommand OpenFollowedCommand { get; }

	public AsyncCommand OpenExpertCommand { get; }

	/// <summary>Hands this journey back to the app that asked for a pick (contract).</summary>
	public AsyncCommand HandOffCommand { get; }

	/// <summary>Replaces a ride by the previous alternative the provider knows (tr/prevnextmove).</summary>
	public AsyncCommand<LegRow> LegEarlierCommand { get; }

	public AsyncCommand<LegRow> LegLaterCommand { get; }

	/// <summary>Opens the live page for the line of a ride.</summary>
	public AsyncCommand<LegRow> ShowLiveCommand { get; }

	public bool HasLiveVehicles =>
		_providers.Supports(ProviderCapabilities.LiveVehicles);

	/// <summary>Shows the whole journey on a map.</summary>
	public AsyncCommand OpenMapCommand { get; }

	/// <summary>Asks the provider again and shows the same journey with its current times (pull to refresh and the toolbar button).</summary>
	public AsyncCommand RefreshCommand { get; }

	public bool IsRefreshing
	{
		get;
		private set => SetProperty(ref field, value);
	}

	/// <summary>Opens the printable version of the journey (tr/trippdf).</summary>
	public AsyncCommand OpenDocumentCommand { get; }

	/// <summary>
	/// A single ride can be swapped for the previous or next one: by the provider's own continuation when it has one,
	/// otherwise by searching again (every provider), so the buttons are there for every journey with a ride.
	/// </summary>
	public bool HasLegAlternatives =>
		_query is not null
		&& _journey is { } journey
		&& journey.Legs.Any(leg => leg.IsRide);

	/// <summary>The journey's actions as icons for its overview card.</summary>
	public JourneyActions Actions { get; }

	/// <summary>Set by the page: brings the notices into view (the badge in the card).</summary>
	public Action? ScrollToNotices { get; set; }

	/// <summary>Following failed or is unavailable: the message stands alone (a followed journey shows it in its strip).</summary>
	public bool ShowTrackingProblem =>
		!IsFollowed && !string.IsNullOrWhiteSpace(TrackingStatus);

	private void RefreshActions()
	{
		JourneyStrings journey = _localization.CurrentStrings.Journey;
		TrackingStrings tracking = _localization.CurrentStrings.Tracking;

		Actions.HasPdf = HasDocument;
		Actions.HasHandOff = IsHandOffAvailable;
		Actions.CanFollow = IsTrackingAvailable;
		Actions.IsFollowed = IsFollowed;
		Actions.CanPause = CanPause;
		Actions.IsPaused = IsPaused;

		Actions.PdfDescription = journey.OpenPdf;
		Actions.HandOffDescription = journey.HandOff;
		Actions.FollowDescription = IsFollowed ? tracking.StopFollowing : journey.FollowJourney;
		Actions.PauseDescription = IsPaused ? tracking.Resume : journey.DeactivateTracking;

		Actions.Touch();
	}

	private Task ToggleFollowAsync() =>
		IsFollowed
			? StopFollowingAsync()
			: FollowJourneyAsync();

	private Task TogglePauseAsync() =>
		IsPaused || CanPause
			? SetPausedAsync(!IsPaused)
			: Task.CompletedTask;

	public bool HasDocument =>
		_query is not null
		&& _journey is not null
		&& _providers.Supports(ProviderCapabilities.JourneyExtras)
		&& _journeys.GetJourneyDocumentUri(_query, _journey) is not null;

	/// <summary>Result of the last alternative lookup ("shown", "none found"); empty otherwise.</summary>
	public string AlternativeStatus
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasAlternativeStatus));
			}
		}
	} = string.Empty;

	public bool HasAlternativeStatus =>
		AlternativeStatus.Length > 0;

	/// <summary>True while another app waits for the user to choose a journey.</summary>
	public bool IsHandOffAvailable =>
		_contract.IsPicking
		&& _journey is not null;

	/// <summary>Why the hand-over failed; empty otherwise.</summary>
	public string HandOffStatus
	{
		get;

		private set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(HasHandOffStatus));
			}
		}
	} = string.Empty;

	public bool HasHandOffStatus =>
		HandOffStatus.Length > 0;

	private void OnContractChanged(
		object? sender,
		EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					OnPropertyChanged(
						nameof(IsHandOffAvailable));

					RefreshActions();
				}
			});

	private async Task HandOffAsync()
	{
		if (_journey is null)
		{
			return;
		}

		HandOffStatus = string.Empty;

		HandOffResult result =
			await _contract.HandOffAsync(_journey);

		if (result != HandOffResult.Sent)
		{
			HandOffStatus =
				_localization
					.CurrentStrings
					.Journey
					.HandOffFailed;
		}

		OnPropertyChanged(
			nameof(IsHandOffAvailable));

		RefreshActions();
	}

	public bool ExpertViewEnabled => _settings.ExpertView;

	/// <summary>The technical-details icon in the page header: only while the expert view is enabled.</summary>
	public IconGlyph ExpertGlyph => ExpertViewEnabled ? IconGlyph.Code : IconGlyph.None;

	public bool IsTrackingAvailable =>
		_tracker.IsAvailable
		&& _providers.Supports(ProviderCapabilities.Tracking);

	/// <summary>The watchlist entry of this journey, if the user follows it.</summary>
	public bool IsFollowed => _followed is not null;

	public bool IsPaused => _followed?.Status == WatchStatus.Deactivated;

	public bool CanPause => IsFollowed && !IsPaused && _followed?.Status != WatchStatus.Recent;

	public bool CanFollow => IsTrackingAvailable && !IsFollowed;

	public string? TrackingStatus
	{
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(ShowTrackingProblem));
			}
		}
	}

	private WatchedJourney? _followed;

	/// <summary>Starts listening for watchlist changes while the page is visible.</summary>
	public void StartObservingTracking()
	{
		if (_trackingObservation is not null || !_tracker.IsAvailable)
		{
			return;
		}

		_trackingObservation = new CancellationTokenSource();

		_tracker.WatchedChanged += OnWatchedChanged;

		UpdateFollowState();

		_ = LoadFollowStateAsync(_trackingObservation.Token);
	}

	protected override void OnDisposing()
	{
		_alternative?.Cancel();
		StopObservingTracking();
	}

	/// <summary>The page's clock: rides that have started or ended since the last tick change their state.</summary>
	public void TickClock()
	{
		foreach (LegRow row in Rows.OfType<LegRow>())
		{
			row.RefreshActive();
		}
	}

	public void StopObservingTracking()
	{
		_tracker.WatchedChanged -= OnWatchedChanged;

		_trackingObservation?.Cancel();
		_trackingObservation?.Dispose();
		_trackingObservation = null;
	}

	private async Task LoadFollowStateAsync(CancellationToken cancellationToken)
	{
		try
		{
			await _tracker.RefreshAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			// Offline: the cached watchlist is still shown.
			DiagnosticLog.Write($"Watchlist refresh failed: {ex.Message}");
		}
	}

	private void OnWatchedChanged(object? sender, EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(UpdateFollowState);

	private void UpdateFollowState()
	{
		_followed = _journey is null ? null : _tracker.Find(_journey);

		OnPropertyChanged(nameof(IsFollowed));
		OnPropertyChanged(nameof(IsPaused));
		OnPropertyChanged(nameof(CanPause));
		OnPropertyChanged(nameof(CanFollow));
		OnPropertyChanged(nameof(ShowTrackingProblem));

		RefreshActions();

		FollowJourneyCommand.RaiseCanExecuteChanged();
		PauseCommand.RaiseCanExecuteChanged();
		ResumeCommand.RaiseCanExecuteChanged();
		StopFollowingCommand.RaiseCanExecuteChanged();

		TrackingStrings strings = _localization.CurrentStrings.Tracking;

		TrackingStatus =
			_followed switch
			{
				null => null,
				{ Phase: TrackingPhase.Planned or TrackingPhase.InProgress } => strings.Following,
				{ } followed => TrackedJourneysViewModel.PhaseText(followed.Phase, strings)
			};
	}

	/// <summary>The journeys that are followed for this one: its parts when it was joined, else itself.</summary>
	private static IReadOnlyList<Journey> PiecesOf(Journey journey) =>
		journey.Parts is { Count: > 1 } parts
			? parts
			: (List<Journey>)[journey];

	/// <summary>The plans of everything followed for the journey on show.</summary>
	private List<string> FollowedPlanIds() =>
		_journey is null
			? []
			: [.. PiecesOf(_journey).Select(piece => _tracker.Find(piece)?.PlanId).OfType<string>().Distinct()];

	private async Task FollowJourneyAsync()
	{
		if (_journey is null)
		{
			return;
		}

		TrackingStrings strings = _localization.CurrentStrings.Tracking;

		// An alternative to a followed journey replaces it (the service links the two, the original is hidden).
		string? replaces =
			_alternativeOf is { } original && !ReferenceEquals(original, _journey)
				? _tracker.Find(original)?.PlanId
				: null;

		WatchedJourney watched = await _tracker.FollowAsync(_journey, replaces, default);

		// A journey through a stop-over that was joined from two parts is followed as two plans, one per part.
		IReadOnlyList<Journey> pieces = PiecesOf(_journey);

		for (int index = 0; index < pieces.Count; index++)
		{
			WatchedJourney? plan = index == 0 ? watched : _tracker.Find(pieces[index]);

			if (plan is null)
			{
				continue;
			}

			// The followed journey's map (and its vehicles) needs the rides with their boarding stops.
			FollowedRides.Save(plan.PlanId, pieces[index]);

			// A missed connection is recovered from these ends; the notification's search needs them.
			if (pieces.Count == 1)
			{
				FollowedEndpoints.Save(plan.PlanId, _query?.From, _query?.To, _query?.Via);
			}
			else if (index == 0)
			{
				FollowedEndpoints.Save(plan.PlanId, _query?.From, _query?.Via, null);
			}
			else
			{
				FollowedEndpoints.Save(plan.PlanId, _query?.Via, _query?.To, null);
			}
		}

		UpdateFollowState();

		if (!await _tracker.CanNotifyAsync())
		{
			TrackingStatus = $"{TrackingStatus} {strings.NotificationsDenied}";
		}
	}

	private async Task SetPausedAsync(bool paused)
	{
		if (_followed is null)
		{
			return;
		}

		foreach (string planId in FollowedPlanIds())
		{
			await _tracker.SetActiveAsync(planId, !paused);
		}

		UpdateFollowState();
	}

	private async Task StopFollowingAsync()
	{
		if (_followed is null)
		{
			return;
		}

		foreach (string planId in FollowedPlanIds())
		{
			await _tracker.DeleteAsync(planId);
		}

		UpdateFollowState();
	}

	private Task OpenExpertAsync() =>
		_journey is null
			? Task.CompletedTask
			: Shell.Current.GoToAsync(
				Routes.Expert,
				new Dictionary<string, object>
				{
					[Routes.JourneyData] = _journey
				});

	// Beside the journey as a pane in a wide window (see Panes).
	private Task OpenFollowedAsync() =>
		Panes.GoToAsync(Routes.Tracked, [], this);

	private void ShowTrackingError(Exception ex) =>
		TrackingStatus =
			string.Format(
				CultureInfo.CurrentCulture,
				_localization.CurrentStrings.Tracking.FollowFailed,
				ex.Message);



	/// <summary>Start and destination, split so the header can put the city under the name.</summary>
	public string FromName
	{
		get;
		private set => SetProperty(
			ref field,
			value);
	} = string.Empty;


	public string? FromPlace
	{
		get;
		private set => SetProperty(
			ref field,
			value);
	}


	public string ToName
	{
		get;
		private set => SetProperty(
			ref field,
			value);
	} = string.Empty;


	public string? ToPlace
	{
		get;
		private set => SetProperty(
			ref field,
			value);
	}


	public ObservableCollection<TimelineRow> Rows { get; } =
		[];


	public JourneyCardModel? Summary
	{
		get;

		private set =>
			SetProperty(
				ref field,
				value);
	}


	public string RouteText
	{
		get;

		private set =>
			SetProperty(
				ref field,
				value);
	} =
		string.Empty;


	public string DayText
	{
		get;

		private set =>
			SetProperty(
				ref field,
				value);
	} =
		string.Empty;


	public IReadOnlyList<NoticeRow> Notices
	{
		get;

		private set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(HasNotices));
			}
		}
	} =
		[];


	public bool HasNotices =>
		Notices.Count > 0;


	/// <summary>Tickets and prices the provider quotes (empty when it quotes none).</summary>
	public IReadOnlyList<FareRow> Fares
	{
		get;

		private set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(HasFares));
			}
		}
	} =
		[];


	public bool HasFares =>
		Fares.Count > 0;


	/// <summary>The section starts collapsed: the preferred ticket is in its header, the rest on demand.</summary>
	public bool FaresExpanded
	{
		get;

		private set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(FaresChevronRotation));

				OnPropertyChanged(
					nameof(FaresToggleDescription));
			}
		}
	}


	public double FaresChevronRotation =>
		FaresExpanded
			? 180
			: 0;


	public string FaresToggleDescription =>
		FaresExpanded
			? _localization.CurrentStrings.Extras.FaresCollapse
			: _localization.CurrentStrings.Extras.FaresExpand;


	/// <summary>The ticket for the passenger set in the options, named under the section title.</summary>
	public string FaresSummaryName
	{
		get;

		private set =>
			SetProperty(
				ref field,
				value);
	} =
		string.Empty;


	/// <summary>Its price, shown in the section header.</summary>
	public string FaresSummaryPrice
	{
		get;

		private set =>
			SetProperty(
				ref field,
				value);
	} =
		string.Empty;


	/// <summary>"Zones: Dresden, Radebeul": the zones are the same for every ticket, so they are said once.</summary>
	public string? FaresZonesText
	{
		get;

		private set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(HasFaresZones));
			}
		}
	}


	public bool HasFaresZones =>
		!string.IsNullOrWhiteSpace(FaresZonesText);


	/// <summary>The conditions the provider prints with its tickets, each once.</summary>
	public string? FaresNotesText
	{
		get;

		private set
		{
			if (SetProperty(
					ref field,
					value))
			{
				OnPropertyChanged(
					nameof(HasFaresNotes));
			}
		}
	}


	public bool HasFaresNotes =>
		!string.IsNullOrWhiteSpace(FaresNotesText);


	public Command ToggleFaresCommand { get; }

	public AsyncCommand<FareRow> OpenFareCommand { get; }


	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.JourneyData,
				out object? value)
			&& value is Journey journey)
		{
			if (query.TryGetValue(Routes.Query, out object? asked)
				&& asked is JourneyQuery journeyQuery)
			{
				_query = journeyQuery;
				Bookmark.Refresh();
			}

			_alternativeOf = null;

			Load(journey);
		}
	}


	private async Task ShowAlternativeAsync(
		LegRow row,
		bool previous)
	{
		if (row?.Source is not { } leg
			|| _journey is not { } journey
			|| _query is not { } query)
		{
			return;
		}

		int index = -1;

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			if (ReferenceEquals(journey.Legs[i], leg))
			{
				index = i;

				break;
			}
		}

		if (index < 0)
		{
			return;
		}

		CancellationTokenSource? earlier = _alternative;

		earlier?.Cancel();

		var cts = new CancellationTokenSource();
		_alternative = cts;

		JourneyStrings strings =
			_localization.CurrentStrings.Journey;

		AlternativeStatus = strings.LegSearching;

		try
		{
			JourneyResult result =
				await _journeys.GetLegAlternativeAsync(
					query,
					journey,
					index,
					previous,
					cts.Token);

			if (cts.IsCancellationRequested || IsDisposed)
			{
				return;
			}

			if (result.Outcome == JourneyOutcome.Failed)
			{
				DiagnosticLog.Write($"[Leg alternative] failed: {result.ErrorMessage} {result.ErrorDetail}");
			}

			// The one journey that was asked for; a journey that is the same as the one on screen is no answer.
			Journey? next = result.Journeys.Count > 0 ? result.Journeys[0] : null;

			if (next is null
				|| ReferenceEquals(next, journey))
			{
				AlternativeStatus = strings.LegNone;

				return;
			}

			_alternativeOf ??= journey;

			Load(next);
			AlternativeStatus = strings.AlternativeShown;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Leg alternative failed: {ex}");

			AlternativeStatus = ex.Message;
		}
		finally
		{
			// Only the request that is still the current one frees the slot; each one disposes its own source.
			if (ReferenceEquals(_alternative, cts))
			{
				_alternative = null;
			}

			cts.Dispose();
		}
	}


	private async Task ShowLiveAsync(
		LegRow row)
	{
		if (!row.CanShowLive)
		{
			return;
		}

		try
		{
			var parameters =
				new ShellNavigationQueryParameters
				{
					[Routes.Line] = row.LineNumber
				};

			// The leg that was tapped is the run to follow: line, direction and a course with times, from its stops
			// or, when the provider gives no stop positions, from its geometry. Without one the live page would
			// show every vehicle of the line, so a leg that cannot be followed opens nothing.
			if (row.Source is { } leg)
			{
				// The passenger usually looks before the vehicle has reached the boarding stop, so the whole run is
				// looked up (the departure at the boarding stop that is this leg, and its stops before and after).
				// The leg's own course is the fallback.
				AlternativeStatus = _localization.CurrentStrings.Extras.LiveConnecting;

				TrackTarget? target = null;

				try
				{
					target =
						await _runs.ResolveAsync(
							leg,
							row.LineNumber,
							_settings.TimeoutSeconds);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					DiagnosticLog.Write($"Looking up the run of line {row.LineNumber} failed: {ex.Message}");
				}

				bool fromRun = target is not null;

				target ??= TrackTargets.FromLeg(leg, row.LineNumber);

				AlternativeStatus = string.Empty;

				DiagnosticLog.Write(
					$"[Live] leg {(_journey is { } shown ? shown.Legs.ToList().FindIndex(item => ReferenceEquals(item, leg)) : -1)}: line {row.LineNumber} "
					+ $"{leg.From.Name} {leg.ScheduledDeparture:HH:mm} -> {leg.To.Name}, course from {(fromRun ? "the run of the departure" : "the leg")}: "
					+ $"{(target is { Course.Count: > 0 } ? $"{target.Course[0].Name} -> {target.Course[^1].Name} ({target.Course.Count} points)" : "none")}");

				if (target is not null)
				{
					parameters[Routes.Track] = target;
				}
				else
				{
					DiagnosticLog.Write($"No course to follow for line {row.LineNumber}: {leg.Stops.Count} stops, {leg.Path.Count} path points");

					AlternativeStatus = _localization.CurrentStrings.Extras.TrackNoCourse;

					return;
				}
			}

			// Beside this page as a pane in a wide window (see Panes).
			await Panes.GoToAsync(
				Routes.Vehicles,
				parameters,
				this);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening the live page failed: {ex.Message}");
		}
	}


	private async Task RefreshAsync()
	{
		if (_journey is not { } current
			|| _query is not { } query)
		{
			return;
		}

		IsRefreshing = true;

		try
		{
			// The same connection again: a search that starts a little before it, and the answer that is this journey
			// (by its timetable identity, else by its planned start and end).
			DateTimeOffset start = JourneyWindow.PlannedStart(current) ?? query.DateTime;
			DateTimeOffset? end = JourneyWindow.PlannedEnd(current);
			string key = JourneyWindow.IdentityOf(current);

			JourneyResult result =
				await _journeys.SearchAsync(
					new JourneyQuery
					{
						From = query.From,
						To = query.To,
						Via = query.Via,
						DateTime = start - TimeSpan.FromMinutes(2),
						SearchMode = JourneySearchMode.Departure,
						MaxResults = 6,
						TimeoutSeconds = query.TimeoutSeconds,
						Routing = query.Routing
					});

			Journey? fresh =
				result.Journeys.FirstOrDefault(item => JourneyWindow.IdentityOf(item) == key)
				?? result.Journeys.FirstOrDefault(item => JourneyWindow.PlannedStart(item) == JourneyWindow.PlannedStart(current) && JourneyWindow.PlannedEnd(item) == end);

			if (fresh is null)
			{
				AlternativeStatus = _localization.CurrentStrings.Extras.JourneyRefreshFailed;

				return;
			}

			AlternativeStatus = string.Empty;

			Load(fresh);
		}
		finally
		{
			IsRefreshing = false;
		}
	}


	private async Task OpenMapAsync()
	{
		if (_journey is not { } journey)
		{
			return;
		}

		try
		{
			ExtrasStrings strings = _localization.CurrentStrings.Extras;

			if (!await MapScenes.OpenAsync(
					MapScenes.FromJourney(journey),
					strings.MapJourneyTitle,
					this))
			{
				AlternativeStatus = strings.MapNoData;
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening the map failed: {ex.Message}");

			AlternativeStatus = ex.Message;
		}
	}


	private async Task OpenDocumentAsync()
	{
		if (_query is not { } query
			|| _journey is not { } journey)
		{
			return;
		}

		try
		{
			// The address itself is refused when opened in a browser (HTTP 403); the app fetches the PDF
			// and hands the file to the system viewer.
			JourneyDocument? document =
				await _journeys.GetJourneyDocumentAsync(query, journey);

			if (document is null)
			{
				AlternativeStatus = _localization.CurrentStrings.Extras.PdfFailed;

				return;
			}

			string path =
				System.IO.Path.Combine(
					FileSystem.CacheDirectory,
					document.FileName);

			await File.WriteAllBytesAsync(path, document.Content);

			await Launcher.Default.OpenAsync(
				new OpenFileRequest(
					document.FileName,
					new ReadOnlyFile(path, "application/pdf")));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening the journey document failed: {ex.Message}");

			AlternativeStatus = ex.Message;
		}
	}


	private TimelineOptions? _builtOptions;

	private TimelineOptions CurrentOptions() =>
		new(
			_settings.ShowWalkingLegs,
			_settings.ExpandNotices,
			_settings.ShowTechnicalDetails,
			_settings.ShowOccupancy,
			_settings.ShowPlatforms,
			_settings.ExpandStops);

	/// <summary>Rebuilds the timeline if a display setting changed since it was built.</summary>
	public void RefreshFromSettings()
	{
		OnPropertyChanged(nameof(ExpertGlyph));

		if (_journey is not null
			&& _builtOptions != CurrentOptions())
		{
			RefreshLocalizedDisplay();
		}
	}

	private void Load(
		Journey journey)
	{
		if (ReferenceEquals(
				_journey,
				journey))
		{
			return;
		}


		try
		{
			_journey =
				journey;
			UpdateFollowState();

			OnPropertyChanged(
				nameof(IsHandOffAvailable));

			OnPropertyChanged(nameof(HasLegAlternatives));
			OnPropertyChanged(nameof(HasDocument));

			RefreshActions();

			LoadError =
				null;

			BuildLocalizedDisplay(
				journey);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"Journey display failed:\n{ex}");


			LoadError =
				_localization
					.CurrentStrings
					.Journey
					.CouldNotBeDisplayed;
		}
	}


	/// <summary>
	/// The tickets as compact rows (the passenger's own ticket first), with what they share said once:
	/// zones and conditions.
	/// </summary>
	private void BuildFares(
		Journey journey)
	{
		ExtrasStrings strings =
			_localization.CurrentStrings.Extras;

		// The journey's own tickets, else the ones borrowed for this very journey.
		IReadOnlyList<JourneyFare> quoted =
			journey.Fares.Count > 0
				? journey.Fares
				: _borrowedFares is { } borrowed && ReferenceEquals(borrowed.Journey, journey)
					? borrowed.Fares
					: journey.Fares;

		JourneyFare? preferred =
			FareChoice.Preferred(
				quoted,
				_settings.Passenger);

		string NameOf(
			JourneyFare fare) =>
			fare.Kind switch
			{
				FareKind.Single => strings.FareSingle,
				FareKind.Day => strings.FareDay,
				_ => fare.Name
			};

		string? WhoOf(
			JourneyFare fare) =>
			fare.Passengers.Count > 0
				? string.Join(
					", ",
					fare.Passengers
						.Select(
							who => OperatingDaysText.Passenger(
								who,
								strings)))
				: fare.ValidFor;

		Fares =
			(List<FareRow>)
			[.. quoted
				.OrderBy(
					fare => ReferenceEquals(fare, preferred)
						? 0
						: 1)
				.ThenBy(
					fare => fare.Kind == FareKind.Single
						? 0
						: fare.Kind == FareKind.Day
							? 2
							: 1)
				.ThenBy(
					fare => fare.Price)
				.Select(
					fare => new FareRow
					{
						Name =
							NameOf(
								fare),

						PriceText =
							fare.Price is { } price
								? Format.Price(
									price,
									fare.Currency)
								: string.Empty,

						Detail =
							string.Join(
								" · ",
								new[]
								{
									fare.Description,
									WhoOf(
										fare)
								}.Where(
									part => !string.IsNullOrWhiteSpace(
										part))) is { Length: > 0 } detail
								? detail
								: null,

						IsPreferred =
							ReferenceEquals(
								fare,
								preferred),

						Link =
							FareLinks.For(
								fare,
								_providers.SelectedId)
					})];

		FaresSummaryName =
			preferred is null
				? string.Empty
				: string.Join(
					" · ",
					new[]
					{
						NameOf(
							preferred),
						WhoOf(
							preferred)
					}.Where(
						part => !string.IsNullOrWhiteSpace(
							part)));

		FaresSummaryPrice =
			preferred is { Price: { } best }
				? Format.Price(
					best,
					preferred.Currency)
				: string.Empty;

		string zones =
			string.Join(
				", ",
				quoted
					.Select(
						fare => fare.Zones)
					.Where(
						text => !string.IsNullOrWhiteSpace(
							text))
					.Distinct());

		FaresZonesText =
			zones.Length > 0
				? $"{strings.FareZones}: {zones}"
				: null;

		string notes =
			string.Join(
				" ",
				quoted
					.Select(
						fare => fare.Notes)
					.Where(
						text => !string.IsNullOrWhiteSpace(
							text))
					.Distinct());

		FaresNotesText =
			notes.Length > 0
				? notes
				: null;

		OnPropertyChanged(
			nameof(FaresToggleDescription));
	}


	/// <summary>
	/// A provider that quotes no tickets still gets a fare card: the same connection is looked up at the VVO
	/// WebAPI (see <see cref="FareBorrower"/>) and its tickets are shown when it is found.
	/// </summary>
	private async Task BorrowFaresAsync(Journey journey)
	{
		_borrowing?.Cancel();

		if (!_fareBorrower.Wants(journey))
		{
			return;
		}

		var cancel = new CancellationTokenSource();

		_borrowing = cancel;

		try
		{
			IReadOnlyList<JourneyFare> fares = await _fareBorrower.BorrowAsync(journey, cancel.Token);

			if (fares.Count == 0
				|| cancel.IsCancellationRequested
				|| IsDisposed
				|| !ReferenceEquals(_journey, journey))
			{
				return;
			}

			_borrowedFares = (journey, fares);

			await MainThread.InvokeOnMainThreadAsync(() => BuildFares(journey));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Fares] borrowing for the journey page failed: {ex.Message}");
		}
	}

	private void BuildLocalizedDisplay(
		Journey journey)
	{
		Summary =
			new JourneyCardModel(
				journey)
			{
				Passenger = _settings.Passenger,
				MapCommand = OpenMapCommand,
				Actions = Actions
			};


		Station start =
			journey.Origin ?? journey.From;

		Station end =
			journey.Destination ?? journey.To;

		RouteText =
			$"{StopLabel.Compose(start)} " +
			$"\u2192 {StopLabel.Compose(end)}";

		FromName =
			start.Name;

		FromPlace =
			StopLabel.PlaceFor(
				start.Name,
				start.Place);

		ToName =
			end.Name;

		ToPlace =
			StopLabel.PlaceFor(
				end.Name,
				end.Place);


		DayText =
			journey.Departure is { } departure
				? Format.ToWall(departure)
					.ToString(
						"dddd, d MMMM",
						CultureInfo.CurrentCulture)
				: string.Empty;


		// Journey-level notices that a leg or transfer already
		// carries would otherwise show twice.
		var nested =
			journey.Legs
				.SelectMany(
					l => l.Notices)
				.Concat(
					journey.Transfers
						.SelectMany(
							t => t.Notices))
				.ToHashSet();


		var options = CurrentOptions();

		_builtOptions = options;


		BuildFares(
			journey);

		_ = BorrowFaresAsync(journey);


		Notices =
			(List<NoticeRow>)
			[.. journey.Notices
				.Where(
					n =>
						!string.IsNullOrWhiteSpace(n)
						&& !nested.Contains(n))
				.Distinct()
				.Select(
					n =>
						new NoticeRow
						{
							Text =
								n,

							Description =
								n,

							Expanded =
								options.ExpandNotices,

							Technical =
								options.Technical
						})];


		Rows.Clear();


		foreach (TimelineRow row in
			TimelineRowFactory.Build(
				journey,
				options))
		{
			if (row is LegRow leg)
			{
				leg.CanSwapRide = HasLegAlternatives;
			}

			Rows.Add(row);
		}


		LoadError =
			null;
	}


	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					RefreshLocalizedDisplay();
				}
			});
	}


	private void RefreshLocalizedDisplay()
	{
		if (_journey is null)
		{
			return;
		}


		try
		{
			BuildLocalizedDisplay(
				_journey);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"Localized journey refresh failed:\n{ex}");


			LoadError =
				_localization
					.CurrentStrings
					.Journey
					.CouldNotBeDisplayed;
		}
	}


	/// <summary>
	/// Inserts or removes a leg's intermediate stops directly below its row.
	/// </summary>
	private void ToggleStops(
		LegRow? leg)
	{
		try
		{
			int at =
				leg is null
					? -1
					: Rows.IndexOf(
						leg);


			if (leg is null
				|| at < 0
				|| !leg.HasIntermediates)
			{
				return;
			}


			if (leg.IsExpanded)
			{
				// Remove only what this leg inserted,
				// never a neighbour.
				for (
					int i = 0;
					i < leg.Intermediates.Count
					&& at + 1 < Rows.Count
					&& Rows[at + 1] is IntermediateRow;
					i++)
				{
					Rows.RemoveAt(
						at + 1);
				}
			}
			else
			{
				for (
					int i = 0;
					i < leg.Intermediates.Count;
					i++)
				{
					leg.Intermediates[i].Index =
						i;


					Rows.Insert(
						at + 1 + i,
						leg.Intermediates[i]);
				}
			}


			leg.IsExpanded =
				!leg.IsExpanded;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"Toggle stops failed:\n{ex}");
		}
	}


	/// <summary>
	/// Asks how to share (title, cancel label, options; returns the chosen option or null).
	/// Set by the page.
	/// </summary>
	public Func<string, string, string[], Task<string?>>? ChooseShareFormat { get; set; }

	/// <summary>Text for messengers, or a rendered picture of the journey.</summary>
	private async Task ShareAsync()
	{
		if (_journey is null)
		{
			return;
		}

		IUiStrings strings = _localization.CurrentStrings;
		JourneyStrings text = strings.Journey;

		try
		{
			JourneyShareModel model = JourneyShareModel.Create(_journey, strings, _settings.Passenger);

			string? choice =
				ChooseShareFormat is null
					? text.ShareAsText
					: await ChooseShareFormat(
						text.ShareTitle,
						strings.Common.Cancel,
						[text.ShareAsText, text.ShareAsImage, text.ShareAsCalendar]);

			if (choice == text.ShareAsImage)
			{
				string path = await JourneyShareImage.RenderToFileAsync(model, strings);

				await Share.Default.RequestAsync(
					new ShareFileRequest
					{
						Title = text.ShareTitle,
						File = new ShareFile(path, "image/png")
					});
			}
			else if (choice == text.ShareAsCalendar)
			{
				string? file =
					JourneyCalendar.Build(
						_journey,
						$"{model.Origin} \u2192 {model.Destination}",
						JourneyShareText.Build(model, strings),
						_settings.DefaultLeadMinutes,
						DateTimeOffset.UtcNow);

				if (file is null)
				{
					AlternativeStatus = text.CalendarUnavailable;

					return;
				}

				string path = Path.Combine(FileSystem.CacheDirectory, "journey.ics");

				await File.WriteAllTextAsync(path, file, new System.Text.UTF8Encoding(false));

				await Share.Default.RequestAsync(
					new ShareFileRequest
					{
						Title = text.ShareAsCalendar,
						File = new ShareFile(path, JourneyCalendar.MediaType)
					});
			}
			else if (choice == text.ShareAsText)
			{
				await Share.Default.RequestAsync(
					new ShareTextRequest
					{
						Title = text.ShareTitle,
						Text = JourneyShareText.Build(model, strings)
					});
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"Share failed:\n{ex}");
		}
	}
}
