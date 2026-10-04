using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Mapping;
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


/// <summary>A quick pick for a line: tapping it adds or removes the line from the entry field.</summary>
public sealed class LineChip : ObservableObject
{
	public LineChip(int line)
	{
		Line = line;
	}

	public int Line { get; }

	public string Text =>
		Line.ToString(CultureInfo.CurrentCulture);

	public bool IsSelected
	{
		get => field;
		internal set => SetProperty(ref field, value);
	}
}


/// <summary>Live positions of the vehicles of chosen lines (TLMS), on a map and in a list.</summary>
public sealed class VehiclesViewModel : DisposableViewModel, IQueryAttributable
{
	private const int MaxRows = 300;

	/// <summary>How long to wait for the first position before saying that none arrived.</summary>
	private static readonly TimeSpan EmptyAfter = TimeSpan.FromSeconds(12);

	/// <summary>Dresden's tram lines, offered as quick picks.</summary>
	private static readonly int[] TramLines = [1, 2, 3, 4, 6, 7, 8, 9, 11, 12, 13];

	private readonly VehicleService _vehicles;
	private readonly LocalizationService _localization;
	private readonly Dictionary<string, VehicleRow> _byKey = [];
	private CancellationTokenSource? _stream;
	private DateTimeOffset _startedAt;
	private bool _dirty;
	private bool _fitNext;

	public VehiclesViewModel(
		VehicleService vehicles)
	{
		ArgumentNullException.ThrowIfNull(vehicles);

		_vehicles = vehicles;
		_localization = LocalizationService.Current;

		Chips = [.. TramLines.Select(line => new LineChip(line))];

		StartCommand =
			new AsyncCommand(
				StartAsync);

		StopCommand =
			new Command(
				Stop);

		FocusCommand =
			new Command<VehicleRow>(
				row =>
				{
					if (row is not null)
					{
						FocusRequested?.Invoke(this, row.Key);
					}
				});

		ToggleChipCommand =
			new AsyncCommand<LineChip>(
				ToggleChipAsync);

		AllLinesCommand =
			new AsyncCommand(
				async () =>
				{
					LineFilter = string.Empty;
					OnPropertyChanged(nameof(LineFilter));

					await StartAsync();
				});

		Rows.CollectionChanged +=
			(_, _) =>
			{
				OnPropertyChanged(nameof(CountText));
				OnPropertyChanged(nameof(HasRows));
			};
	}

	/// <summary>A new picture of the map is ready (the page hands it to the map).</summary>
	public event EventHandler<MapScene>? SceneChanged;

	/// <summary>The map should centre on this vehicle (<see cref="LiveVehicle.Key"/>).</summary>
	public event EventHandler<string>? FocusRequested;

	public AsyncCommand StartCommand { get; }

	public Command StopCommand { get; }

	public Command<VehicleRow> FocusCommand { get; }

	public AsyncCommand<LineChip> ToggleChipCommand { get; }

	public AsyncCommand AllLinesCommand { get; }

	public IReadOnlyList<LineChip> Chips { get; }

	public ObservableCollection<VehicleRow> Rows { get; } = [];

	public bool HasRows =>
		Rows.Count > 0;

	public string CountText =>
		Rows.Count == 0
			? string.Empty
			: string.Format(
				CultureInfo.CurrentCulture,
				_localization.CurrentStrings.Extras.LiveCount,
				Rows.Count);

	public string LineFilter
	{
		get => field;

		set
		{
			if (SetProperty(ref field, value ?? string.Empty))
			{
				SyncChips();
			}
		}
	} = string.Empty;

	/// <summary>Entries that were not line numbers and were left out; empty when the input was fine.</summary>
	public string InputHint
	{
		get => field;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasInputHint));
			}
		}
	} = string.Empty;

	public bool HasInputHint =>
		InputHint.Length > 0;

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

	/// <summary>Called by the page's timer: texts move on, the map gets the newest positions.</summary>
	public void Tick()
	{
		foreach (VehicleRow row in Rows)
		{
			row.Refresh();
		}

		if (_dirty)
		{
			PublishScene();
		}

		if (IsStreaming
			&& Rows.Count == 0
			&& HasStatus
			&& DateTimeOffset.UtcNow - _startedAt > EmptyAfter)
		{
			Status = _localization.CurrentStrings.Extras.LiveEmpty;
		}
	}

	private void PublishScene()
	{
		_dirty = false;

		bool fit = _fitNext && Rows.Count > 0;

		if (fit)
		{
			_fitNext = false;
		}

		SceneChanged?.Invoke(
			this,
			MapScenes.FromVehicles(
				Rows.Select(row => row.Vehicle),
				fit));
	}

	private static (IReadOnlyList<int> Lines, IReadOnlyList<string> Ignored) ParseLines(string text)
	{
		var lines = new List<int>();
		var ignored = new List<string>();

		foreach (string part in
			text.Split(
				[',', ';', ' '],
				StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int line))
			{
				if (!lines.Contains(line))
				{
					lines.Add(line);
				}
			}
			else
			{
				ignored.Add(part);
			}
		}

		return (lines, ignored);
	}

	private void SyncChips()
	{
		IReadOnlyList<int> lines = ParseLines(LineFilter).Lines;

		foreach (LineChip chip in Chips)
		{
			chip.IsSelected = lines.Contains(chip.Line);
		}
	}

	private async Task ToggleChipAsync(LineChip chip)
	{
		if (chip is null)
		{
			return;
		}

		var lines = ParseLines(LineFilter).Lines.ToList();

		if (!lines.Remove(chip.Line))
		{
			lines.Add(chip.Line);
		}

		lines.Sort();

		LineFilter = string.Join(", ", lines);
		OnPropertyChanged(nameof(LineFilter));

		await StartAsync();
	}

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

		(IReadOnlyList<int> lines, IReadOnlyList<string> ignored) = ParseLines(LineFilter);

		ExtrasStrings strings = _localization.CurrentStrings.Extras;

		InputHint =
			ignored.Count == 0
				? string.Empty
				: string.Format(CultureInfo.CurrentCulture, strings.LiveInvalid, string.Join(", ", ignored));

		_startedAt = DateTimeOffset.UtcNow;
		_fitNext = true;
		_dirty = false;

		SceneChanged?.Invoke(this, new MapScene { Fit = false });

		IsStreaming = true;
		Status = strings.LiveConnecting;

		try
		{
			await foreach (LiveVehicle vehicle in
				_vehicles.StreamAsync(
					new VehicleFilter { Lines = lines },
					cts.Token))
			{
				if (cts.IsCancellationRequested || IsDisposed)
				{
					return;
				}

				if (HasStatus)
				{
					Status = string.Empty;
				}

				Apply(vehicle);
			}

			if (!cts.IsCancellationRequested)
			{
				Status = strings.LiveError;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Live vehicles failed: {ex}");

			Status = strings.LiveError;
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
		_dirty = true;

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

	protected override void OnDisposing() =>
		Stop();
}
