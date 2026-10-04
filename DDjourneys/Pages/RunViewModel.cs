using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One stop of the vehicle's run.</summary>
public sealed record RunRow(
	RunStop Stop)
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

	public bool IsPassed =>
		Stop.Position == RunPosition.Previous;

	public bool IsCurrent =>
		Stop.Position == RunPosition.Current;

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
	}

	public AsyncCommand RefreshCommand { get; }

	public ObservableCollection<RunRow> Rows { get; } = [];

	public string Title
	{
		get => field;
		private set => SetProperty(ref field, value);
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

			foreach (RunStop stop in stops)
			{
				Rows.Add(new RunRow(stop));
			}

			Message =
				Rows.Count == 0
					? _localization.CurrentStrings.Departures.NoRun
					: string.Empty;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Run failed: {ex}");

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
