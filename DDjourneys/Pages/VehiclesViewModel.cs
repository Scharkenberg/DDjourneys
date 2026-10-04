using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One vehicle on the live list; updated in place as new positions arrive.</summary>
public sealed class VehicleRow : ObservableObject
{
	public VehicleRow(LiveVehicle vehicle)
	{
		ArgumentNullException.ThrowIfNull(vehicle);

		Vehicle = vehicle;
		Key = vehicle.Key;
	}

	public string Key { get; }

	public LiveVehicle Vehicle
	{
		get => field;

		private set => field = value;
	}

	public string LineText =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.Extras.LiveLine,
			Vehicle.Line);

	public string RunText =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.Extras.LiveRun,
			Vehicle.Run);

	public string DelayText
	{
		get
		{
			if (Vehicle.Delay is not { } delay)
			{
				return string.Empty;
			}

			return Format.Delay(delay)
				?? LocalizationService.Current.CurrentStrings.Extras.LiveOnTime;
		}
	}

	public bool HasDelay =>
		Vehicle.Delay is not null;

	public string SourceText
	{
		get
		{
			ExtrasStrings strings =
				LocalizationService.Current.CurrentStrings.Extras;

			return Vehicle.Source switch
			{
				VehicleSource.Gps => strings.LiveSourceGps,
				VehicleSource.Telegram => strings.LiveSourceTelegram,
				_ => string.Empty
			};
		}
	}

	public string AgeText
	{
		get
		{
			ExtrasStrings strings =
				LocalizationService.Current.CurrentStrings.Extras;

			double seconds =
				Math.Max(0, (DateTimeOffset.UtcNow - Vehicle.Time).TotalSeconds);

			return seconds < 90
				? string.Format(CultureInfo.CurrentCulture, strings.LiveSecondsAgo, (int)seconds)
				: string.Format(CultureInfo.CurrentCulture, strings.LiveMinutesAgo, (int)(seconds / 60));
		}
	}

	public string Description =>
		$"{LineText}, {RunText}, {DelayText}, {AgeText}";

	public Uri MapUri =>
		new(
			string.Create(
				CultureInfo.InvariantCulture,
				$"https://www.openstreetmap.org/?mlat={Vehicle.Latitude:F6}&mlon={Vehicle.Longitude:F6}#map=17/{Vehicle.Latitude:F6}/{Vehicle.Longitude:F6}"));

	public void Update(LiveVehicle vehicle)
	{
		ArgumentNullException.ThrowIfNull(vehicle);

		if (vehicle.Time < Vehicle.Time)
		{
			return;
		}

		Vehicle = vehicle;
		Refresh();
	}

	/// <summary>Re-reads every text (a new position, or just time passing).</summary>
	public void Refresh()
	{
		OnPropertyChanged(nameof(DelayText));
		OnPropertyChanged(nameof(HasDelay));
		OnPropertyChanged(nameof(SourceText));
		OnPropertyChanged(nameof(AgeText));
		OnPropertyChanged(nameof(Description));
	}
}


/// <summary>Live positions of the vehicles of chosen lines (TLMS).</summary>
public sealed class VehiclesViewModel : DisposableViewModel, IQueryAttributable
{
	private const int MaxRows = 300;

	private readonly VehicleService _vehicles;
	private readonly LocalizationService _localization;
	private readonly Dictionary<string, VehicleRow> _byKey = [];
	private CancellationTokenSource? _stream;

	public VehiclesViewModel(
		VehicleService vehicles)
	{
		ArgumentNullException.ThrowIfNull(vehicles);

		_vehicles = vehicles;
		_localization = LocalizationService.Current;

		StartCommand =
			new AsyncCommand(
				StartAsync);

		StopCommand =
			new Command(
				Stop);

		OpenMapCommand =
			new AsyncCommand<VehicleRow>(
				OpenMapAsync);
	}

	public AsyncCommand StartCommand { get; }

	public Command StopCommand { get; }

	public AsyncCommand<VehicleRow> OpenMapCommand { get; }

	public ObservableCollection<VehicleRow> Rows { get; } = [];

	public string LineFilter
	{
		get => field;
		set => SetProperty(ref field, value ?? string.Empty);
	} = string.Empty;

	public bool IsStreaming
	{
		get => field;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(IsIdle));
			}
		}
	}

	public bool IsIdle =>
		!IsStreaming;

	public string Status
	{
		get => field;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasStatus));
			}
		}
	} = string.Empty;

	public bool HasStatus =>
		Status.Length > 0;

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.Line, out object? value)
			&& value is string line
			&& !string.IsNullOrWhiteSpace(line))
		{
			LineFilter = line;
			OnPropertyChanged(nameof(LineFilter));

			_ = StartAsync();
		}
	}

	/// <summary>Called by the page's timer: the "x s ago" texts move on.</summary>
	public void Tick()
	{
		foreach (VehicleRow row in Rows)
		{
			row.Refresh();
		}
	}

	private static IReadOnlyList<int> ParseLines(string text) =>
		[.. text
			.Split(
				[',', ';', ' '],
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int line) ? line : -1)
			.Where(line => line >= 0)
			.Distinct()];

	private async Task StartAsync()
	{
		Stop();

		if (IsDisposed)
		{
			return;
		}

		var cts = new CancellationTokenSource();
		_stream = cts;

		Rows.Clear();
		_byKey.Clear();

		IsStreaming = true;
		Status = _localization.CurrentStrings.Extras.LiveConnecting;

		try
		{
			bool first = true;

			await foreach (LiveVehicle vehicle in
				_vehicles.StreamAsync(
					new VehicleFilter { Lines = ParseLines(LineFilter) },
					cts.Token))
			{
				if (cts.IsCancellationRequested || IsDisposed)
				{
					return;
				}

				if (first)
				{
					first = false;
					Status = string.Empty;
				}

				Apply(vehicle);
			}

			if (!cts.IsCancellationRequested)
			{
				Status = _localization.CurrentStrings.Extras.LiveError;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Live vehicles failed: {ex}");

			Status = _localization.CurrentStrings.Extras.LiveError;
		}
		finally
		{
			if (ReferenceEquals(_stream, cts))
			{
				IsStreaming = false;
			}
		}
	}

	private void Apply(LiveVehicle vehicle)
	{
		if (_byKey.TryGetValue(vehicle.Key, out VehicleRow? row))
		{
			row.Update(vehicle);

			return;
		}

		if (Rows.Count >= MaxRows)
		{
			return;
		}

		row = new VehicleRow(vehicle);
		_byKey[row.Key] = row;

		// Keep the list ordered by line, then run, so rows do not jump around.
		int index = 0;

		while (index < Rows.Count
			&& (Rows[index].Vehicle.Line, Rows[index].Vehicle.Run).CompareTo((vehicle.Line, vehicle.Run)) < 0)
		{
			index++;
		}

		Rows.Insert(index, row);
	}

	private void Stop()
	{
		_stream?.Cancel();
		_stream = null;
		IsStreaming = false;
	}

	private async Task OpenMapAsync(VehicleRow row)
	{
		try
		{
			await Launcher.Default.OpenAsync(row.MapUri);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Opening the map failed: {ex.Message}");
		}
	}

	protected override void OnDisposing() =>
		Stop();
}
