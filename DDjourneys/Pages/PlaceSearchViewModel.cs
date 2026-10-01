using System.Collections.ObjectModel;
using System.Diagnostics;
using DDjourneys.Core.Api;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
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
	private enum MessageKind
	{
		None,
		Hint,
		TooShort,
		NoResults,
		ServiceUnavailable
	}

	private const int MinQueryLength = 2;
	private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

	private readonly LocationService _locations;
	private readonly PlaceStore _store;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	// Lifetime rule: every search owns its source and disposes it in its own finally block.
	// Everyone else may only Cancel() it (guarded), never Dispose() it.
	private CancellationTokenSource? _search;
	private bool _targetIsFrom;
	private bool _isNavigating;
	private MessageKind _messageKind;

	public PlaceSearchViewModel(
		LocationService locations,
		PlaceStore store,
		AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(locations);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);

		_locations = locations;
		_store = store;
		_settings = settings;
		_localization = LocalizationService.Current;

		_localization.PropertyChanged += OnLocalizationChanged;

		SelectPlaceCommand =
			new AsyncCommand<Location>(SelectPlaceAsync);

		try
		{
			foreach (Location place in _store.Recents)
			{
				Recents.Add(new PlaceRow(place));
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine(
				$"Recents unavailable: {ex.Message}");
		}

		SetMessage(
			Recents.Count == 0
				? MessageKind.Hint
				: MessageKind.None);
	}

	public AsyncCommand<Location> SelectPlaceCommand { get; }

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
	public bool ShowRecents =>
		Query.Trim().Length == 0 && Recents.Count > 0;

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.TargetIsFrom,
				out object? value)
			&& value is bool isFrom)
		{
			_targetIsFrom = isFrom;
		}
	}

	/// <summary>Stops a pending search (page left). Safe to call any time.</summary>
	public void Cancel()
	{
		CancelCurrent();
		IsSearching = false;
	}

	private void CancelCurrent()
	{
		CancellationTokenSource? current = _search;
		_search = null;

		try
		{
			current?.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// Its search already finished and disposed it.
		}
	}

	private void OnQueryChanged()
	{
		CancelCurrent();

		string text = Query.Trim();

		if (text.Length < MinQueryLength)
		{
			IsSearching = false;
			Results.Clear();

			SetMessage(
				text.Length switch
				{
					0 when Recents.Count == 0 =>
						MessageKind.Hint,

					0 =>
						MessageKind.None,

					_ =>
						MessageKind.TooShort
				});

			RefreshState();
			return;
		}

		var cts = _search = new CancellationTokenSource();

		Results.Clear();
		SetMessage(MessageKind.None);
		RefreshState();

		_ = SearchAsync(text, cts);
	}

	/// <summary>
	/// Newest search wins. Never throws: it is started fire-and-forget.
	/// The awaits resume on the UI thread, so the collections may be changed here.
	/// </summary>
	private async Task SearchAsync(
		string text,
		CancellationTokenSource cts)
	{
		CancellationToken token = cts.Token;

		try
		{
			await Task.Delay(Debounce, token);

			IsSearching = true;

			IReadOnlyList<Location> found =
				await _locations.SearchAsync(
					text,
					token,
					TimeSpan.FromSeconds(_settings.TimeoutSeconds));

			if (token.IsCancellationRequested
				|| !ReferenceEquals(_search, cts))
			{
				return;
			}

			Results.Clear();

			foreach (Location place in found)
			{
				Results.Add(new PlaceRow(place));
			}

			SetMessage(
				Results.Count == 0
					? MessageKind.NoResults
					: MessageKind.None);
		}
		catch (OperationCanceledException)
			when (token.IsCancellationRequested)
		{
			// Superseded by a newer query.
		}
		catch (Exception ex)
		{
			// Includes provider errors and HTTP timeouts (cancellation without our token).
			Debug.WriteLine(
				$"Place search failed:\n{ex}");

			if (ReferenceEquals(_search, cts))
			{
				Results.Clear();

				if (ex is ApiException
					{
						Message.Length: > 0
					} api)
				{
					// Keep provider-supplied text unchanged.
					SetMessage(MessageKind.None, api.Message);
				}
				else
				{
					SetMessage(
						MessageKind.ServiceUnavailable);
				}
			}
		}
		finally
		{
			if (ReferenceEquals(_search, cts))
			{
				_search = null;
				IsSearching = false;
				RefreshState();
			}

			cts.Dispose();
		}
	}

	private async Task SelectPlaceAsync(Location place)
	{
		if (_isNavigating)
		{
			return;
		}

		_isNavigating = true;

		try
		{
			try
			{
				_store.AddRecent(place);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(
					$"Remembering the place failed: {ex.Message}");
			}

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
			Debug.WriteLine(
				$"Returning the chosen place failed:\n{ex}");
		}
		finally
		{
			_isNavigating = false;
		}
	}

	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ApplyLocalizedMessage();
			RefreshState();
		});
	}

	private void SetMessage(
		MessageKind kind,
		string? text = null)
	{
		_messageKind = kind;

		Message = text ?? kind switch
		{
			MessageKind.Hint =>
				_localization.CurrentStrings.PlaceSearch.Hint,

			MessageKind.TooShort =>
				_localization.CurrentStrings.PlaceSearch.TypeAtLeastTwoCharacters,

			MessageKind.NoResults =>
				_localization.CurrentStrings.PlaceSearch.NoPlacesFound,

			MessageKind.ServiceUnavailable =>
				_localization.CurrentStrings.PlaceSearch.CouldNotReachService,

			_ =>
				text ?? string.Empty
		};
	}

	private void ApplyLocalizedMessage()
	{
		if (_messageKind == MessageKind.None)
		{
			return;
		}

		SetMessage(_messageKind);
	}

	private void RefreshState()
	{
		OnPropertyChanged(nameof(HasMessage));
		OnPropertyChanged(nameof(HasResults));
		OnPropertyChanged(nameof(ShowRecents));
	}
}