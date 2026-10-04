using DDjourneys.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One stop of the vehicle's run.</summary>
public sealed record RunRow(
	RunStop Stop,
	bool IsPassed = false,
	bool IsCurrent = false)
{
	public string Name =>
		Stop.Station.Name;

	public string? Place =>
		StopLabel.PlaceFor(
			Stop.Station.Name,
			Stop.Station.Place);

	public string TimeText =>
		Stop.Effective is { } time
			? Format.Time(time)
			: string.Empty;

	public string? DelayText =>
		Stop.IsCancelled
			? null
			: Format.Delay(Stop.Delay);

	public bool HasDelay =>
		DelayText is not null;

	public bool IsCancelled =>
		Stop.IsCancelled;

	public string CancelledText =>
		LocalizationService.Current.CurrentStrings.Departures.Cancelled;

	public string? CurrentText =>
		IsCurrent
			? LocalizationService.Current.CurrentStrings.Departures.VehicleHere
			: null;

	public OccupancyLevel Occupancy =>
		Stop.Occupancy;

	public bool HasOccupancy =>
		Occupancy != OccupancyLevel.Unknown;

	public double RowOpacity =>
		IsPassed ? 0.55 : 1;

	public string Description =>
		$"{Name}, {TimeText}"
		+ (DelayText is { } delay ? $", {delay}" : string.Empty)
		+ (CurrentText is { } here ? $", {here}" : string.Empty);
}


/// <summary>The stops a departing vehicle serves (dm/trip), with the position of the vehicle.</summary>
public sealed class RunViewModel : DisposableViewModel, IQueryAttributable
{
	private readonly DepartureService _departures;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;
	private Departure? _departure;
	private CancellationTokenSource? _load;

	public RunViewModel(
		DepartureService departures,
		AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(departures);
		ArgumentNullException.ThrowIfNull(settings);

		_departures = departures;
		_settings = settings;
		_localization = LocalizationService.Current;

		RefreshCommand =
			new AsyncCommand(
				LoadAsync);

		ShowLiveCommand =
			new AsyncCommand(
				ShowLiveAsync);

		OpenMapCommand =
			new AsyncCommand(
				OpenMapAsync);
	}

	public AsyncCommand RefreshCommand { get; }

	public AsyncCommand ShowLiveCommand { get; }

	/// <summary>Shows the stops of the run (and where the vehicle is) on a map.</summary>
	public AsyncCommand OpenMapCommand { get; }

	/// <summary>At least two stops of the run have a position.</summary>
	public bool CanShowMap =>
		Rows.Count(row => row.Stop.Station.Latitude is not null && row.Stop.Station.Longitude is not null) >= 2;

	/// <summary>The line is a plain number, so its vehicles can be looked up on the live page.</summary>
	public bool CanShowLive =>
		int.TryParse(
			Title.Trim(),
			NumberStyles.None,
			CultureInfo.InvariantCulture,
			out _);

	public ObservableCollection<RunRow> Rows { get; } = [];

	public string Title
	{
		get => field;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(CanShowLive));
			}
		}
	} = string.Empty;

	public string? Direction
	{
		get => field;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasDirection));
			}
		}
	}

	public bool HasDirection =>
		!string.IsNullOrWhiteSpace(Direction);

	public ChipLook? Look
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	public bool IsBusy
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	public string Message
	{
		get => field;

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

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.DepartureData, out object? value)
			&& value is Departure departure)
		{
			_departure = departure;

			Title =
				string.IsNullOrWhiteSpace(departure.Line.Name)
					? Format.TransportMode(departure.Line.Mode)
					: departure.Line.Name;

			Direction = departure.Line.Destination;
			Look = ModeChips.For(departure.Line.Mode);

			_ = LoadAsync();
		}
	}

	private async Task OpenMapAsync()
	{
		if (_departure is not { } departure)
		{
			return;
		}

		try
		{
			bool shown =
				await MapScenes.OpenAsync(
					MapScenes.FromRun(
						[.. Rows.Select(row => row.Stop)],
						departure.Line.Mode,
						Rows.ToList().FindIndex(row => row.IsCurrent)),
					Title);

			if (!shown)
			{
				Message = _localization.CurrentStrings.Extras.MapNoData;
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening the map failed: {ex.Message}");
		}
	}

	private async Task ShowLiveAsync()
	{
		if (!CanShowLive)
		{
			return;
		}

		try
		{
			var parameters =
				new ShellNavigationQueryParameters
				{
					[Routes.Line] = Title.Trim()
				};

			// This run, not the whole line: the live page picks its vehicle out of the line's by the course.
			TrackTarget target =
				new()
				{
					Line = Title.Trim(),
					Mode = _departure?.Line.Mode ?? TransitMode.Unknown,
					Direction = Direction,
					Course =
						[.. Rows
							.Where(row => row.Stop.Station.Latitude is not null && row.Stop.Station.Longitude is not null)
							.Select(
								row => new CoursePoint(
									row.Stop.Station.Latitude!.Value,
									row.Stop.Station.Longitude!.Value,
									row.Stop.Effective,
									row.Name))]
				};

			if (target.IsUsable)
			{
				parameters[Routes.Track] = target;
			}

			await Shell.Current.GoToAsync(
				Routes.Vehicles,
				parameters);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening the live page failed: {ex.Message}");
		}
	}

	private async Task LoadAsync()
	{
		if (_departure is not { } departure || IsDisposed)
		{
			return;
		}

		_load?.Cancel();

		var cts = new CancellationTokenSource();
		_load = cts;
		IsBusy = true;

		try
		{
			IReadOnlyList<RunStop> stops =
				await _departures.GetRunAsync(
					departure,
					_settings.TimeoutSeconds,
					cts.Token);

			if (cts.IsCancellationRequested || IsDisposed)
			{
				return;
			}

			Rows.Clear();

			// One journey, and the vehicle where its times say it is (not at the stop the search started from).
			IReadOnlyList<RunStop> course =
				RunCourse.Isolate(
					stops,
					departure.Scheduled);

			int here =
				RunCourse.VehicleIndex(
					course,
					DateTimeOffset.UtcNow);

			for (int i = 0; i < course.Count; i++)
			{
				Rows.Add(new RunRow(course[i], i < here, i == here));
			}

			Message =
				Rows.Count == 0
					? departure.Effective < DateTimeOffset.UtcNow
						? _localization.CurrentStrings.Extras.RunDeparted
						: _localization.CurrentStrings.Departures.NoRun
					: string.Empty;

			OnPropertyChanged(nameof(CanShowMap));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Run failed: {ex}");

			Message =
				string.IsNullOrWhiteSpace(ex.Message)
					? _localization.CurrentStrings.Common.SomethingWentWrong
					: ex.Message;
		}
		finally
		{
			if (ReferenceEquals(_load, cts))
			{
				IsBusy = false;
			}
		}
	}

	protected override void OnDisposing() =>
		_load?.Cancel();
}
