using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
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


	public JourneyViewModel(
		AppSettings settings,
		IJourneyTracker tracker)
	{
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
		FollowJourneyCommand = new AsyncCommand(FollowJourneyAsync, () => _journey is not null && _tracker.IsAvailable, ShowTrackingError);
		DeactivateTrackingCommand = new AsyncCommand(DeactivateTrackingAsync, () => IsTracking, ShowTrackingError);
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
	public AsyncCommand DeactivateTrackingCommand { get; }
	public bool IsTrackingAvailable => _tracker.IsAvailable;
	public bool IsTracking { get; private set { if (SetProperty(ref field, value)) { DeactivateTrackingCommand.RaiseCanExecuteChanged(); OnPropertyChanged(nameof(CanFollow)); } } }
	public bool CanFollow => IsTrackingAvailable && !IsTracking;
	public string? TrackingStatus { get; private set => SetProperty(ref field, value); }

	public void StartObservingTracking()
	{
		if (_trackingObservation is not null) return;
		_trackingObservation = new CancellationTokenSource();
		_ = ObserveTrackingAsync(_trackingObservation.Token);
	}

	public void StopObservingTracking()
	{
		_trackingObservation?.Cancel();
		_trackingObservation?.Dispose();
		_trackingObservation = null;
	}

	private async Task ObserveTrackingAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var update in _tracker.Events.WithCancellation(cancellationToken))
			{
				MainThread.BeginInvokeOnMainThread(() =>
				{
					IsTracking = update.Kind is not (JourneyTrackingEventKind.Cancelled or JourneyTrackingEventKind.Arrived);
					TrackingStatus = update.State.Message ?? update.State.Phase switch
					{
						TrackingPhase.AtRisk => "Connection at risk",
						TrackingPhase.Cancelled => "Journey cancelled",
						TrackingPhase.Arrived => "Arrived",
						_ => "Journey tracking is active. Progress appears in your notification shade."
					};
				});
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
	}

	private async Task FollowJourneyAsync()
	{
		if (_journey is null) return;
		await _tracker.StartAsync(_journey);
		IsTracking = true;
		TrackingStatus = "Journey tracking is active. Progress appears in your notification shade.";
	}

	private async Task DeactivateTrackingAsync()
	{
		await _tracker.StopAsync();
		IsTracking = false;
		TrackingStatus = "Journey tracking paused.";
	}

	private void ShowTrackingError(Exception ex) => TrackingStatus = $"Journey tracking could not be started: {ex.Message}";


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
			_settings.ShowTechnicalDetails);

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
			FollowJourneyCommand.RaiseCanExecuteChanged();

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


		RouteText =
			$"{journey.From.Name} " +
			$"\u2192 {journey.To.Name}";


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
							$"{ride.Leg.From.Name} " +
							$"\u2192 {ride.Leg.To.Name} " +
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
						walk.Leg.To.Name);

					break;
			}
		}


		return string.Join(
			Environment.NewLine,
			lines);
	}
}
