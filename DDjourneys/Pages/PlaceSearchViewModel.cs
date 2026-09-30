using System.Collections.ObjectModel;
using System.Diagnostics;
using DDjourneys.Core.Services;
using DDjourneys.Core.Storage;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>Display-ready row of the place list (results and recents).</summary>
public sealed record PlaceRow(Location Place)
{
	public string Name => Place.Name;
	public string Detail => Place.Place ?? string.Empty;
	public bool HasDetail => !string.IsNullOrWhiteSpace(Place.Place);
}

public sealed class PlaceSearchViewModel : ObservableObject, IQueryAttributable
{
	private const int MinQueryLength = 2;
	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

	private readonly LocationService _locations;
	private readonly PlaceStore _store;
	private readonly AppSettings _settings;

	private CancellationTokenSource? _search;
	private bool _targetIsFrom;
	private bool _isNavigating;

	public PlaceSearchViewModel(LocationService locations, PlaceStore store, AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(locations);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);

		_locations = locations;
		_store = store;
		_settings = settings;

		SelectPlaceCommand = new Command<Location>(async place => await SelectPlaceAsync(place));

		foreach (Location place in _store.Recents)
		{
			Recents.Add(new PlaceRow(place));
		}

		Message = Recents.Count == 0 ? "Type a station, stop or address." : string.Empty;
	}

	public Command<Location> SelectPlaceCommand { get; }

	public ObservableCollection<PlaceRow> Results { get; } = [];
	public ObservableCollection<PlaceRow> Recents { get; } = [];

	public string Query
	{
		get => field;
		set
		{
			if (SetProperty(ref field, value ?? string.Empty))
			{
				OnQueryChanged();
			}
		}
	} = string.Empty;

	public bool IsSearching
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	/// <summary>Hint, "nothing found" or error. Empty when there is nothing to say.</summary>
	public string Message
	{
		get => field;
		private set => SetProperty(ref field, value);
	} = string.Empty;

	public bool HasMessage => Message.Length > 0;
	public bool HasResults => Results.Count > 0;
	public bool ShowRecents => Query.Trim().Length == 0 && Recents.Count > 0;

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.TargetIsFrom, out object? value) && value is bool isFrom)
		{
			_targetIsFrom = isFrom;
		}
	}

	private void OnQueryChanged()
	{
		CancellationTokenSource? previous = _search;
		if (previous is not null)
		{
			previous.Cancel();
			_ = DisposeWhenFinishedAsync(previous);
		}
		_search = null;

		string text = Query.Trim();

		if (text.Length < MinQueryLength)
		{
			IsSearching = false;
			Results.Clear();

			Message = text.Length switch
			{
				0 => Recents.Count == 0 ? "Type a station, stop or address." : string.Empty,
				_ => "Type at least 2 characters."
			};

			RefreshState();
			return;
		}

		var cts = _search = new CancellationTokenSource();
		Results.Clear();
		RefreshState();
		_ = SearchAsync(text, cts);
	}

	private static async Task DisposeWhenFinishedAsync(CancellationTokenSource source)
	{
		try { await source.CancelAsync(); }
		catch (ObjectDisposedException) { }
		finally { source.Dispose(); }
	}

	/// <summary>
	/// Newest search wins. Never throws: it is started fire-and-forget.
	/// The awaits resume on the UI thread, so the collections may be changed here.
	/// </summary>
	private async Task SearchAsync(string text, CancellationTokenSource cts)
	{
		CancellationToken token = cts.Token;

		try
		{
			await Task.Delay(Debounce, token);

			IsSearching = true;

			var found = await _locations.SearchAsync(text, token, TimeSpan.FromSeconds(_settings.TimeoutSeconds));

			token.ThrowIfCancellationRequested();

			var rows = found.Select(static place => new PlaceRow(place)).ToArray();
			MainThread.BeginInvokeOnMainThread(() =>
			{
				if (!token.IsCancellationRequested && ReferenceEquals(_search, cts))
				{
					Results.Clear();
					foreach (PlaceRow row in rows) Results.Add(row);
					Message = Results.Count == 0 ? "No places found." : string.Empty;
				}
			});
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// Superseded by a newer query.
			return;
		}
		catch (Exception ex)
		{
			// Includes HTTP timeouts (TaskCanceledException without our token).
			Debug.WriteLine($"Place search failed: {ex}");

			if (ReferenceEquals(_search, cts))
			{
				Results.Clear();
				Message = "Could not reach the timetable service. Check your connection.";
			}
		}
		finally
		{
			if (ReferenceEquals(_search, cts))
			{
				IsSearching = false;
				RefreshState();
				cts.Dispose();
			}
		}
	}

	private async Task SelectPlaceAsync(Location? place)
	{
		if (place is null || _isNavigating)
		{
			return;
		}

		_isNavigating = true;

		try
		{
			_store.AddRecent(place);

			await Shell.Current.GoToAsync(
				"..",
				new ShellNavigationQueryParameters
				{
					[Routes.SelectedPlace] = place,
					[Routes.TargetIsFrom] = _targetIsFrom
				});
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"Returning the chosen place failed: {ex}");
		}
		finally
		{
			_isNavigating = false;
		}
	}

	private void RefreshState()
	{
		OnPropertyChanged(nameof(HasMessage));
		OnPropertyChanged(nameof(HasResults));
		OnPropertyChanged(nameof(ShowRecents));
	}
}
