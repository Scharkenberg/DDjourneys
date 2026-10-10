using DDjourneys.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Mapping;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One vehicle on the live list; updated in place as new positions arrive.</summary>
public sealed partial class VehicleRow : ObservableObject
{
	public VehicleRow(LiveVehicle vehicle)
	{
		ArgumentNullException.ThrowIfNull(vehicle);

		Vehicle = vehicle;
		Key = vehicle.Key;
	}

	public string Key { get; }

	public LiveVehicle Vehicle { get; private set; }

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


/// <summary>Live positions of the vehicles of chosen lines (TLMS), on a map and in a list.</summary>
public sealed partial class VehiclesViewModel : DisposableViewModel, IQueryAttributable
{
	private const int MaxRows = 300;

	/// <summary>How long to wait for the first position before saying that none arrived.</summary>
	/// <summary>How long to wait for the first position before saying why there may be none: vehicles often report only every minute or two.</summary>
	private static readonly TimeSpan EmptyAfterTracking = TimeSpan.FromMinutes(3);
	private static readonly TimeSpan EmptyAfterLines = TimeSpan.FromMinutes(1);

	/// <summary>How long "Connecting…" may stay up: after that the honest text is that the service is
	/// quiet, not that it is still connecting.</summary>
	private static readonly TimeSpan ConnectingFor = TimeSpan.FromSeconds(10);

	/// <summary>How long after the end of the followed trips "no position" may be said: while any of
	/// them may still be under way, the vehicle may report yet.</summary>
	private static readonly TimeSpan TripOverAfter = TimeSpan.FromMinutes(5);

	private readonly VehicleService _vehicles;
	private readonly VehicleStreamHub _hub;
	private readonly LocalizationService _localization;
	private VehicleStreamSubscription? _vehicleSubscription;
	private readonly Dictionary<string, VehicleRow> _byKey = [];
	private CancellationTokenSource? _stream;
	private DateTimeOffset _startedAt;
	private bool _dirty;
	private bool _fitNext;

	// Following runs: one slot per run (the rides of a followed journey, or the one run of a leg or departure), each with
	// the vehicle matched to it and what the match is based on. _target is the first of them.
	private TrackTarget? _target;
	private readonly List<Slot> _slots = [];

	private sealed class Slot(TrackTarget target)
	{
		public TrackTarget Target { get; } = target;
		public LiveVehicle? Matched { get; set; }
		public double Score { get; set; } = double.MaxValue;
		public DateTimeOffset Seen { get; set; }
	}
	private readonly Dictionary<string, LiveVehicle> _previous = [];

	// The lines of the running stream: a resume continues with them, not with whatever is typed but not started.
	private IReadOnlyList<int> _activeLines = [];

	public VehiclesViewModel(
		VehicleService vehicles,
		VehicleStreamHub hub,
		ProviderRegistry providers)
	{
		ArgumentNullException.ThrowIfNull(vehicles);
		ArgumentNullException.ThrowIfNull(hub);
		ArgumentNullException.ThrowIfNull(providers);

		_vehicles = vehicles;
		_hub = hub;
		_localization = LocalizationService.Current;

		// Positions, lines and the followed run belong to one provider: a switch stops the stream and drops them.
		Subscribe(
			() => providers.SelectionChanged += OnProviderChanged,
			() => providers.SelectionChanged -= OnProviderChanged);

		// The one shared stream tells the page where it stands; the page maps it to its own texts.
		Subscribe(
			() => hub.StateChanged += OnHubStateChanged,
			() => hub.StateChanged -= OnHubStateChanged);

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

		ShowAllCommand =
			new AsyncCommand(
				async () =>
				{
					_target = null;
					_slots.Clear();
					OnPropertyChanged(nameof(IsTracking));
					OnPropertyChanged(nameof(IsNotTracking));
					OnPropertyChanged(nameof(IsIdle));

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

	/// <summary>Leaves the followed run and shows every vehicle of its line.</summary>
	public AsyncCommand ShowAllCommand { get; }

	/// <summary>One run is followed (opened from a journey or a departure), not a whole line.</summary>
	public bool IsNotTracking => !IsTracking;

	public bool IsTracking =>
		_target is not null;

	public string TrackText =>
		_target is { } target
			? string.Format(
				CultureInfo.CurrentCulture,
				_localization.CurrentStrings.Extras.TrackFollowing,
				_slots.Count > 1
					? string.Join(" \u00B7 ", _slots.Select(slot => slot.Target.Line))
					: target.Line,
				_slots.Count > 1
					? string.Empty
					: target.Direction ?? string.Empty).Trim()
			: string.Empty;

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
		get;

		set => SetProperty(ref field, value ?? string.Empty);
	} = string.Empty;

	/// <summary>Entries that were not line numbers and were left out; empty when the input was fine.</summary>
	public string InputHint
	{
		get;

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
		get;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(IsIdle));
			}
		}
	}

	public bool IsIdle =>
		!IsStreaming
		&& _target is null;

	public string Status
	{
		get;

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
		if ((query.TryGetValue(Routes.TrackSet, out object? set) && set is IReadOnlyList<TrackTarget> many
				? many.Where(item => item.IsUsable).ToList()
				: query.TryGetValue(Routes.Track, out object? tracked) && tracked is TrackTarget { IsUsable: true } one
					? [one]
					: []) is { Count: > 0 } targets)
		{
			_slots.Clear();
			_slots.AddRange(targets.Select(item => new Slot(item)));

			_target = targets[0];
			LineFilter = string.Join(", ", targets.Select(item => item.Line).Distinct());

			OnPropertyChanged(nameof(LineFilter));
			OnPropertyChanged(nameof(IsTracking));
					OnPropertyChanged(nameof(IsNotTracking));
			OnPropertyChanged(nameof(TrackText));
			OnPropertyChanged(nameof(IsIdle));

			_ = StartAsync();

			return;
		}

		if (query.TryGetValue(Routes.Line, out object? value)
			&& value is string line
			&& !string.IsNullOrWhiteSpace(line))
		{
			LineFilter = line;
			OnPropertyChanged(nameof(LineFilter));

			_ = StartAsync();
		}
	}

	/// <summary>A new page shows the map: the next tick publishes the scene again.</summary>
	public void RequestScene() =>
		_dirty = true;

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

		ExtrasStrings strings = _localization.CurrentStrings.Extras;

		// A connection that stands up says so for a moment only; after that the honest text is that the
		// service is quiet, not that it is still connecting — vehicles often report only every minute or two.
		if (IsStreaming
			&& Status == strings.LiveConnecting
			&& DateTimeOffset.UtcNow - _startedAt > ConnectingFor)
		{
			Status = strings.LiveWaiting;
		}

		if (!IsStreaming
			|| !HasStatus
			|| Status == strings.LiveError)
		{
			return;
		}

		if (_target is null)
		{
			if (Rows.Count == 0
				&& DateTimeOffset.UtcNow - _startedAt > EmptyAfterLines)
			{
				Status = strings.LiveEmpty;
			}

			return;
		}

		// The followed trips decide how long waiting is honest: while any of them may still be under way
		// (its last stop plus a grace), the vehicle may report yet — a layover at a stop sends nothing for
		// minutes. Only when all of them are over is "no position" more than impatience.
		if (_slots.All(slot => slot.Matched is not null))
		{
			return;
		}

		if (_slots.All(slot => Over(slot.Target)))
		{
			Status = strings.TrackNotFound;
		}
	}

	/// <summary>
	/// Whether the trip a target follows is over: its last stop lies far enough in the past. A course
	/// without times (none in practice) falls back to the flat wait since the stream started.
	/// </summary>
	private bool Over(TrackTarget target)
	{
		DateTimeOffset? last = null;

		foreach (CoursePoint point in target.Course)
		{
			if (point.Time is { } time
				&& (last is null || time > last))
			{
				last = time;
			}
		}

		return last is { } end
			? DateTimeOffset.UtcNow > end + TripOverAfter
			: DateTimeOffset.UtcNow - _startedAt > EmptyAfterTracking;
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
			_slots.Count > 0
				? MapScenes.FromTracks((List<TrackTarget>)[.. _slots.Select(slot => slot.Target)], (List<LiveVehicle?>)[.. _slots.Select(slot => slot.Matched)], false)
				: MapScenes.FromVehicles(
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
		_previous.Clear();

		foreach (Slot slot in _slots)
		{
			slot.Matched = null;
			slot.Score = double.MaxValue;
		}

		(IReadOnlyList<int> lines, IReadOnlyList<string> ignored) = ParseLines(LineFilter);
		_activeLines = lines;

		ExtrasStrings strings = _localization.CurrentStrings.Extras;

		InputHint =
			ignored.Count == 0
				? string.Empty
				: string.Format(CultureInfo.CurrentCulture, strings.LiveInvalid, string.Join(", ", ignored));

		_startedAt = DateTimeOffset.UtcNow;
		_fitNext = true;
		_dirty = false;

		// Following a run: its course is on the map from the start, before any position arrives.
		SceneChanged?.Invoke(
			this,
			_slots.Count > 0
				? MapScenes.FromTracks((List<TrackTarget>)[.. _slots.Select(slot => slot.Target)], (List<LiveVehicle?>)[.. _slots.Select(_ => (LiveVehicle?)null)], true)
				: new MapScene { Fit = false });

		IsStreaming = true;
		Status = strings.LiveConnecting;

		await ConnectAsync(cts, lines);
	}

	/// <summary>
	/// Attaches to the one shared stream (the hub) and applies what arrives until it is cancelled. The
	/// reconnect ladder lives in the hub now; the line filter is applied here, client-side, because the
	/// shared stream is unfiltered - a set lookup per position.
	/// </summary>
	private async Task ConnectAsync(CancellationTokenSource cts, IReadOnlyList<int> lines)
	{
		try
		{
			_vehicleSubscription?.Dispose();
			_vehicleSubscription = _hub.Subscribe();

			await foreach (LiveVehicle vehicle in _vehicleSubscription.Reader.ReadAllAsync(cts.Token))
			{
				if (cts.IsCancellationRequested || IsDisposed)
				{
					return;
				}

				if (_slots.Count > 0)
				{
					ApplyTracked(vehicle);

					continue;
				}

				if (lines.Count > 0 && !lines.Contains(vehicle.Line))
				{
					continue;
				}

				if (HasStatus)
				{
					Status = string.Empty;
				}

				Apply(vehicle);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			_vehicleSubscription?.Dispose();
			_vehicleSubscription = null;

			if (ReferenceEquals(_stream, cts))
			{
				IsStreaming = false;
			}
		}
	}

	/// <summary>
	/// Following runs: every position of a line is scored against the course of each run on that line; the best fit
	/// is shown and kept until another vehicle fits clearly better or the shown one stops reporting.
	/// </summary>
	private void ApplyTracked(LiveVehicle vehicle)
	{
		if (_slots.All(slot => slot.Matched is null)
			&& Status == _localization.CurrentStrings.Extras.LiveConnecting)
		{
			Status = _localization.CurrentStrings.Extras.LiveWaiting;
		}

		_previous.TryGetValue(vehicle.Key, out LiveVehicle? before);

		// Keep the older position as the reference for the direction of travel until it is far enough away.
		if (before is null
			|| RunMatcherMoved(before, vehicle))
		{
			_previous[vehicle.Key] = vehicle;
		}

		foreach (Slot slot in _slots.Where(slot => slot.Target.LineNumber == vehicle.Line))
		{
			ApplyToSlot(slot, vehicle, before);
		}
	}

	private void ApplyToSlot(Slot slot, LiveVehicle vehicle, LiveVehicle? before)
	{
		double? score = RunMatcher.Score(slot.Target, vehicle, before);

		if (slot.Matched is not null
			&& slot.Matched.Key == vehicle.Key)
		{
			if (score is null)
			{
				// The shown vehicle no longer fits (its run ended, or noise): let another one take over.
				slot.Score = double.MaxValue;
			}
			else
			{
				slot.Score = score.Value;
				slot.Matched = vehicle;
				slot.Seen = DateTimeOffset.UtcNow;
				ShowMatched(vehicle);
			}

			return;
		}

		if (score is not { } value)
		{
			return;
		}

		bool stale =
			slot.Matched is null
			|| DateTimeOffset.UtcNow - slot.Seen > TimeSpan.FromSeconds(150)
			|| slot.Score == double.MaxValue;

		if (stale
			|| value + 45 < slot.Score)
		{
			// Only this slot's earlier vehicle leaves the list; other followed runs keep theirs.
			if (slot.Matched is { } old
				&& !_slots.Any(other => other != slot && other.Matched?.Key == old.Key)
				&& _byKey.Remove(old.Key, out VehicleRow? oldRow))
			{
				Rows.Remove(oldRow);
			}

			slot.Matched = vehicle;
			slot.Score = value;
			slot.Seen = DateTimeOffset.UtcNow;

			ShowMatched(vehicle);
		}
	}

	private static bool RunMatcherMoved(
		LiveVehicle from,
		LiveVehicle to) =>
		Math.Abs(from.Latitude - to.Latitude) * 110_540 > 25
		|| Math.Abs(from.Longitude - to.Longitude) * 70_000 > 25;

	private void ShowMatched(LiveVehicle vehicle)
	{
		if (HasStatus)
		{
			Status = string.Empty;
		}

		_dirty = true;

		if (_byKey.TryGetValue(vehicle.Key, out VehicleRow? row))
		{
			row.Update(vehicle);

			return;
		}

		row = new VehicleRow(vehicle);
		_byKey[row.Key] = row;
		Rows.Add(row);
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

	private void OnProviderChanged(object? sender, string providerId) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (IsDisposed)
				{
					return;
				}

				Stop();

				_byKey.Clear();
				_previous.Clear();
				_slots.Clear();
				_target = null;
				Rows.Clear();
			});

	/// <summary>Where the one shared stream stands, as the page says it (connecting, delivering, broken).</summary>
	private void OnHubStateChanged(VehicleStreamState state) =>
			MainThread.BeginInvokeOnMainThread(
					() =>
					{
						if (IsDisposed || !IsStreaming)
						{
							return;
						}

						ExtrasStrings strings = _localization.CurrentStrings.Extras;

						if (state == VehicleStreamState.Degraded)
						{
							Status = strings.LiveError;
						}
						else if (state == VehicleStreamState.Connecting && HasStatus)
						{
							Status = strings.LiveConnecting;
						}
						else if (state == VehicleStreamState.Live && Status == strings.LiveConnecting)
						{
							Status = string.Empty;
						}
						});

	private void Stop()
	{
		_stream?.Cancel();
		_stream = null;
		IsStreaming = false;
	}

	/// <summary>
	/// A page nobody looks at stops reading the stream: the subscription ends, so the hub can close its
	/// socket after the linger. The stream itself stays on - what it shows stays, only the reading pauses.
	/// </summary>
	public void PauseStream()
	{
		if (!IsStreaming)
		{
			return;
		}

		_stream?.Cancel();
		_stream = null;
	}

	/// <summary>A page seen again continues its stream where it was: the lines it had, not what is typed but not started.</summary>
	public void ResumeStream()
	{
		if (!IsStreaming
			|| _stream is not null
			|| IsDisposed)
		{
			return;
		}

		CancellationTokenSource cts = new();
		_stream = cts;

		// The cancellation of the pause detached the subscription; ConnectAsync subscribes again, and the
		// hub reopens its socket with the first reader. What the board shows stays standing while it connects.
		_ = ConnectAsync(cts, _activeLines);
	}

	protected override void OnDisposing() =>
		Stop();
}
