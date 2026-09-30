using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public sealed partial class PlanViewModel : ObservableObject
{
	/// <summary>
	/// A time earlier than now by at most this much is treated as "now".
	/// Anything older is taken to mean the same time tomorrow (23:40 → typing 00:15).
	/// </summary>
	private static readonly TimeSpan RolloverGrace = TimeSpan.FromMinutes(30);

	private readonly PlaceStore _store;
	private readonly AppSettings _settings;
	private DateTime _when; // provider-zone wall clock, minute precision. Single source of truth.
	private bool _syncing;

	public PlanViewModel(PlaceStore store, AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);
		_store = store;
		_settings = settings;
		_store.Changed += OnStoreChanged;

		PickFromCommand = new Command(async () => await SafeAsync(() => PickAsync(true)));
		PickToCommand = new Command(async () => await SafeAsync(() => PickAsync(false)));
		ToggleFromFavouriteCommand = new Command(() => Safe(() => ToggleFavourite(From)));
		ToggleToFavouriteCommand = new Command(() => Safe(() => ToggleFavourite(To)));
		SwapCommand = new Command(() => Safe(Swap));
		DepartCommand = new Command(() => IsArrival = false);
		ArriveCommand = new Command(() => IsArrival = true);
		NowCommand = new Command(() => Safe(SetNow));
		NudgeCommand = new Command<string>(minutes => Safe(() => Nudge(minutes)));
		SearchCommand = new Command(async () => await SafeAsync(SearchAsync), () => CanSearch);
		ClearRecentsCommand = new Command(() => Safe(_store.ClearRecents));

		SetNow();
		if (_settings.DefaultArrival)
		{
			IsArrival = true;
		}

		RefreshPlaces();
	}

	public Func<bool, Task>? OpenPlaceSearch { get; set; }
	public Func<JourneyQuery, Task>? OpenResults { get; set; }
	public Func<string, Task>? ShowError { get; set; }

	private void OnStoreChanged(object? sender, EventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(RefreshPlaces);
	}

	public Command PickFromCommand { get; }
	public Command PickToCommand { get; }
	public Command ToggleFromFavouriteCommand { get; }
	public Command ToggleToFavouriteCommand { get; }
	public Command SwapCommand { get; }
	public Command DepartCommand { get; }
	public Command ArriveCommand { get; }
	public Command NowCommand { get; }
	public Command<string> NudgeCommand { get; }
	public Command SearchCommand { get; }
	public Command ClearRecentsCommand { get; }

	public ObservableCollection<Location> Recents { get; } = [];
	public ObservableCollection<Location> Favourites { get; } = [];

	public Location? From
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(FromText));
				OnPropertyChanged(nameof(HasFrom));
				OnPropertyChanged(nameof(FromStar));
				OnRouteChanged();
			}
		}
	}

	public Location? To
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(ToText));
				OnPropertyChanged(nameof(HasTo));
				OnPropertyChanged(nameof(ToStar));
				OnRouteChanged();
			}
		}
	}

	public string FromText => From?.ToString() ?? "Choose start";
	public string ToText => To?.ToString() ?? "Choose destination";
	public bool HasFrom => From is not null;
	public bool HasTo => To is not null;
	public string FromStar => Star(From);
	public string ToStar => Star(To);

	// ----- When -----

	/// <summary>Bound to the DatePicker. Picking a day keeps the time of day; the past is clamped to now.</summary>
	public DateTime Date
	{
		get => _when.Date;
		set
		{
			if (!_syncing)
			{
				SetWhen(value.Date + _when.TimeOfDay, fromTimeOfDay: false);
			}
		}
	}

	/// <summary>Bound to the TimePicker. A time already past today means the same time tomorrow.</summary>
	public TimeSpan Time
	{
		get => _when.TimeOfDay;
		set
		{
			if (!_syncing)
			{
				SetWhen(_when.Date + TimeSpan.FromMinutes(Math.Floor(value.TotalMinutes)), fromTimeOfDay: true);
			}
		}
	}

	public DateTime MinDate
	{
		get => field;
		private set => SetProperty(ref field, value);
	} = Format.NowLocal().Date;

	public DateTime MaxDate => MinDate.AddYears(1);

	/// <summary>"Tomorrow · 00:15": the one-line truth about what will be searched.</summary>
	public string WhenText =>
		IsNow
			? "Now"
			: $"{Format.DayLabel(_when)} \u00B7 {_when.ToString("HH:mm", CultureInfo.InvariantCulture)}";

	public string ModeText => IsArrival ? "Arrive by" : "Leave";

	public bool IsArrival
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value))
			{
				if (value)
				{
					IsNow = false; // "arrive by now" makes no sense
				}

				OnPropertyChanged(nameof(ModeText));
				OnPropertyChanged(nameof(WhenText));
			}
		}
	}

	/// <summary>True until the user chooses a date or time. Search then uses the moment of searching.</summary>
	public bool IsNow
	{
		get => field;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(IsNotNow));
				OnPropertyChanged(nameof(WhenText));
			}
		}
	}

	public bool IsNotNow => !IsNow;

	public bool HasFavourites => Favourites.Count > 0;
	public bool HasRecents => Recents.Count > 0;
	public bool IsPlacesEmpty => !HasFavourites && !HasRecents;

	public bool CanSearch =>
		From is not null && To is not null && !SamePlace(From, To);

	/// <summary>
	/// Call when the page appears. Handles the app resting past midnight:
	/// refreshes "today" and, in Now mode, the displayed clock.
	/// </summary>
	public void Refresh()
	{
		Safe(() =>
		{
			DateTime now = Format.NowLocal();
			MinDate = now.Date;
			OnPropertyChanged(nameof(MaxDate));

			if (IsNow)
			{
				SetNow();
			}
			else if (_when < now)
			{
				Apply(now); // a stale plan is never searched in the past
			}
		});
	}

	/// <summary>Fills the empty start first, otherwise replaces the destination.</summary>
	public void UsePlace(Location place)
	{
		ArgumentNullException.ThrowIfNull(place);

		if (From is null)
		{
			From = place;
		}
		else
		{
			To = place;
		}
	}

	public JourneyQuery BuildQuery()
	{
		if (From is null || To is null)
		{
			throw new InvalidOperationException("Start and destination are required.");
		}

		DateTime now = Format.NowLocal();
		DateTime target = IsNow ? now : (_when < now ? now : _when);

		return new JourneyQuery
		{
			From = From,
			To = To,
			DateTime = IsNow ? DateTimeOffset.Now : Format.ToOffset(target),
			SearchMode = IsArrival ? JourneySearchMode.Arrival : JourneySearchMode.Departure,
			MaxResults = _settings.MaxResults,
			TimeoutSeconds = _settings.TimeoutSeconds
		};
	}

	// ----- date/time logic -----

	private void SetWhen(DateTime candidate, bool fromTimeOfDay)
	{
		DateTime now = Format.NowLocal();

		if (candidate < now)
		{
			candidate = fromTimeOfDay && candidate.Date == now.Date && now - candidate > RolloverGrace
				? candidate.AddDays(1)
				: now;
		}

		IsNow = false;
		Apply(candidate);
	}

	private void Nudge(string? minutes)
	{
		if (!int.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delta))
		{
			return;
		}

		DateTime basis = IsNow ? Format.NowLocal() : _when;
		DateTime now = Format.NowLocal();
		DateTime next = basis.AddMinutes(delta);

		IsNow = false;
		Apply(next < now ? now : next); // crosses midnight naturally, never enters the past
	}

	private void SetNow()
	{
		Apply(Format.NowLocal());
		IsArrival = false;
		IsNow = true;
	}

	/// <summary>Stores the moment and pushes it to both pickers without re-entering the setters.</summary>
	private void Apply(DateTime value)
	{
		_syncing = true;

		try
		{
			_when = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
			OnPropertyChanged(nameof(Date));
			OnPropertyChanged(nameof(Time));
			OnPropertyChanged(nameof(WhenText));
		}
		finally
		{
			_syncing = false;
		}
	}

	// ----- actions -----

	private async Task PickAsync(bool isFrom)
	{
		if (OpenPlaceSearch is not null)
		{
			await OpenPlaceSearch(isFrom);
		}
	}

	private async Task SearchAsync()
	{
		if (!CanSearch || OpenResults is null)
		{
			return;
		}

		JourneyQuery query = BuildQuery();

		_store.AddRecent(query.To);
		_store.AddRecent(query.From);

		await OpenResults(query);
	}

	private void Swap() => (From, To) = (To, From);

	private void ToggleFavourite(Location? place)
	{
		if (place is not null)
		{
			_store.SetFavourite(place, !_store.IsFavourite(place));
		}
	}

	private string Star(Location? place)
	{
		try
		{
			return place is not null && _store.IsFavourite(place) ? "\u2605" : "\u2606";
		}
		catch (Exception)
		{
			return "\u2606";
		}
	}

	private void OnRouteChanged() => SearchCommand.ChangeCanExecute();

	private void RefreshPlaces()
	{
		Safe(() =>
		{
			Replace(Recents, _store.Recents);
			Replace(Favourites, _store.Favourites);

			OnPropertyChanged(nameof(HasFavourites));
			OnPropertyChanged(nameof(HasRecents));
			OnPropertyChanged(nameof(IsPlacesEmpty));
			OnPropertyChanged(nameof(FromStar));
			OnPropertyChanged(nameof(ToStar));
		});
	}

	private static void Replace(ObservableCollection<Location> target, IReadOnlyList<Location> source)
	{
		target.Clear();

		foreach (Location place in source)
		{
			target.Add(place);
		}
	}

	private static bool SamePlace(Location a, Location b) =>
		a.IsStation && b.IsStation
			? a.Id == b.Id
			: a.Name == b.Name && a.Place == b.Place;

	// ----- error containment: no command may throw into the UI thread -----

	private void Safe(Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Report(ex);
		}
	}

	private async Task SafeAsync(Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Report(ex);
		}
	}

	private void Report(Exception ex)
	{
		System.Diagnostics.Debug.WriteLine($"Plan error:\n{ex}");

		try
		{
			ShowError?.Invoke(ex.Message);
		}
		catch (Exception inner)
		{
			System.Diagnostics.Debug.WriteLine($"Plan error reporting failed: {inner.Message}");
		}
	}
}
