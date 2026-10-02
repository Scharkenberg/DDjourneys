using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Tracking;
using DDjourneys.Localization;
using DDjourneys.Support;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DDjourneys.Pages;

public sealed class JourneyViewModel :
	ObservableObject,
	IQueryAttributable
{
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;
	private readonly IJourneyTracker _tracker;
	private CancellationTokenSource? _trackingObservation;

	private Journey? _journey;


	private readonly ProviderRegistry _providers;


	public JourneyViewModel(
		AppSettings settings,
		IJourneyTracker tracker,
		ProviderRegistry providers)
	{
		_providers = providers ?? throw new ArgumentNullException(nameof(providers));

		ArgumentNullException.ThrowIfNull(
			settings);

		_settings =
			settings;
		_tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

		_localization =
			LocalizationService.Current;


		_localization.PropertyChanged +=
			OnLocalizationChanged;


		ToggleStopsCommand =
			new Command<LegRow>(
				ToggleStops);


		ShareCommand =
			new AsyncCommand(
				ShareAsync);
		FollowJourneyCommand = new AsyncCommand(FollowJourneyAsync, () => _journey is not null && CanFollow, ShowTrackingError);
		PauseCommand = new AsyncCommand(() => SetPausedAsync(true), () => CanPause, ShowTrackingError);
		ResumeCommand = new AsyncCommand(() => SetPausedAsync(false), () => IsPaused, ShowTrackingError);
		StopFollowingCommand = new AsyncCommand(StopFollowingAsync, () => IsFollowed, ShowTrackingError);
		OpenFollowedCommand = new AsyncCommand(OpenFollowedAsync, null, ShowTrackingError);
		OpenExpertCommand = new AsyncCommand(OpenExpertAsync);
	}


	/// <summary>
	/// Set when the journey could not be displayed;
	/// the page shows it instead of crashing.
	/// </summary>
	public string? LoadError
	{
		get => field;

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

	public bool ExpertViewEnabled => _settings.ExpertView;

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
		get => field;
		private set => SetProperty(ref field, value);
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
			System.Diagnostics.Debug.WriteLine($"Watchlist refresh failed: {ex.Message}");
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

	private async Task FollowJourneyAsync()
	{
		if (_journey is null)
		{
			return;
		}

		TrackingStrings strings = _localization.CurrentStrings.Tracking;

		await _tracker.FollowAsync(_journey);

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

		await _tracker.SetActiveAsync(_followed.PlanId, !paused);

		UpdateFollowState();
	}

	private async Task StopFollowingAsync()
	{
		if (_followed is null)
		{
			return;
		}

		await _tracker.DeleteAsync(_followed.PlanId);

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

	private static Task OpenFollowedAsync() =>
		Shell.Current.GoToAsync(Routes.Tracked);

	private void ShowTrackingError(Exception ex) =>
		TrackingStatus =
			string.Format(
				CultureInfo.CurrentCulture,
				_localization.CurrentStrings.Tracking.FollowFailed,
				ex.Message);



	/// <summary>Start and destination, split so the header can put the city under the name.</summary>
	public string FromName
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	} = string.Empty;


	public string? FromPlace
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	}


	public string ToName
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	} = string.Empty;


	public string? ToPlace
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	}


	public ObservableCollection<TimelineRow> Rows { get; } =
		[];


	public JourneyCardModel? Summary
	{
		get => field;

		private set =>
			SetProperty(
				ref field,
				value);
	}


	public string RouteText
	{
		get => field;

		private set =>
			SetProperty(
				ref field,
				value);
	} =
		string.Empty;


	public string DayText
	{
		get => field;

		private set =>
			SetProperty(
				ref field,
				value);
	} =
		string.Empty;


	public IReadOnlyList<NoticeRow> Notices
	{
		get => field;

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


	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.JourneyData,
				out object? value)
			&& value is Journey journey)
		{
			Load(journey);
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

			LoadError =
				null;

			BuildLocalizedDisplay(
				journey);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"Journey display failed:\n{ex}");


			LoadError =
				_localization
					.CurrentStrings
					.Journey
					.CouldNotBeDisplayed;
		}
	}


	private void BuildLocalizedDisplay(
		Journey journey)
	{
		Summary =
			new JourneyCardModel(
				journey);


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


		Notices =
			journey.Notices
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
						})
				.ToList();


		Rows.Clear();


		foreach (TimelineRow row in
			TimelineRowFactory.Build(
				journey,
				options))
		{
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
			RefreshLocalizedDisplay);
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
			System.Diagnostics.Debug.WriteLine(
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
			System.Diagnostics.Debug.WriteLine(
				$"Toggle stops failed:\n{ex}");
		}
	}


	private async Task ShareAsync()
	{
		if (_journey is null)
		{
			return;
		}


		try
		{
			await Share.Default
				.RequestAsync(
					new ShareTextRequest
					{
						Title =
							_localization
								.CurrentStrings
								.Journey
								.ShareTitle,

						Text =
							BuildShareText(
								_journey)
					});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"Share failed:\n{ex}");
		}
	}


	private string BuildShareText(
		Journey journey)
	{
		JourneyStrings strings =
			_localization
				.CurrentStrings
				.Journey;


		var lines =
			new List<string>
			{
				RouteText,

				$"{DayText}, " +
				$"{Format.TimeOrDash(journey.Departure)}" +
				$"\u2013{Format.TimeOrDash(journey.Arrival)} " +
				$"({Format.Duration(journey.Duration)})",

				string.Empty
			};


		foreach (
			TimelineItem item
			in TimelineBuilder.Build(journey))
		{
			switch (item)
			{
				case RideItem ride:
					{
						string line =
							ride.Leg.Line?.Name
							?? Format.TransportMode(
								ride.Leg.Mode);


						lines.Add(
							$"{Format.TimeOrDash(ride.Leg.EffectiveDeparture)} " +
							$"{line}: " +
							$"{StopLabel.Compose(ride.Leg.From)} " +
							$"\u2192 {StopLabel.Compose(ride.Leg.To)} " +
							$"({Format.TimeOrDash(ride.Leg.EffectiveArrival)})");


						break;
					}


				case WalkItem walk:
					lines.Add(
						$"{strings.Walk} " +
						$"{Format.Duration(
							walk.Leg.EffectiveDeparture,
							walk.Leg.EffectiveArrival)} " +
						$"{strings.To} " +
						StopLabel.Compose(walk.Leg.To));

					break;


				case BoundaryItem
				{
					WalkTime: { } boundaryWalk
				} boundary:
					lines.Add(
						$"{strings.Walk} " +
						$"{Format.Duration(boundaryWalk)} " +
						$"{strings.To} " +
						StopLabel.Compose(boundary.At));

					break;
			}
		}


		return string.Join(
			Environment.NewLine,
			lines);
	}
}
