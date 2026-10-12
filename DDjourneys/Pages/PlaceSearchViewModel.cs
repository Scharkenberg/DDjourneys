using DDjourneys.Core.Diagnostics;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using DDjourneys.Core.Api;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>Display-ready row of the place list (results and recents).</summary>
public sealed record PlaceRow(Location Place)
{
	public string Name => StopLabel.NameFor(Place.Name, Place.Place);
	public string Detail => StopLabel.PlaceFor(Place.Name, Place.Place) ?? string.Empty;
	public bool HasDetail => Detail.Length > 0;

	/// <summary>"Address" / "Point of interest"; stops carry no label.</summary>
	public string? KindText
	{
		get
		{
			ExtrasStrings strings =
				LocalizationService.Current.CurrentStrings.Extras;

			return Place.Kind switch
			{
				PlaceKind.Address => strings.KindAddress,
				PlaceKind.Poi => strings.KindPoi,
				_ => null
			};
		}
	}

	public bool HasKind => KindText is not null;
}

/// <summary>
/// A line the app has seen at a stop it loaded (the line search): tapping shows the course of the whole
/// line, exactly like the line rows under "Around this stop".
/// </summary>
public sealed record LineRow(IndexedLine Line)
{
	public string Name =>
		Line.Name;

	public ChipLook Look =>
		ModeChips.For(Line.Mode);

	/// <summary>"Line 11 — show course".</summary>
	public string Title =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.PlaceSearch.LineCourse,
			Line.Name);

	/// <summary>Where the app saw the line (the stop its course is looked up from).</summary>
	public string Detail =>
		string.Format(
			CultureInfo.CurrentCulture,
			LocalizationService.Current.CurrentStrings.PlaceSearch.LineSeenAt,
			Line.SeenAt.Name);
}

public sealed partial class PlaceSearchViewModel : DisposableViewModel, IQueryAttributable
{
	private enum MessageKind
	{
		None,
		Hint,
		TooShort,
		NoResults,
		ServiceUnavailable
	}

	private readonly ProviderRegistry _providers;
	private string _providerId;
	private string CacheKey(string text) =>
	$"{_providerId}\0{(int)Kinds}\0{text}";

	/// <summary>
	/// What this search may return: stops only when the place is for the departure monitor or a stop
	/// over; otherwise what the settings allow.
	/// </summary>
	private PlaceKinds Kinds =>
		_target is Routes.TargetDepartures or Routes.TargetVia
			? PlaceKinds.Stops
			: _target is Routes.TargetStart or Routes.TargetHome
				? PlaceKinds.Stops | PlaceKinds.Addresses | PlaceKinds.Pois
				: _settings.SearchKinds;

	/// <summary>Quiet time after the last keystroke before the endpoint is asked (a setting).</summary>
	private TimeSpan Debounce => TimeSpan.FromMilliseconds(_settings.SearchDelayMs);

	private int MinQueryLength => _settings.MinQueryLength;

	private const int CacheSize = 32;

	// Answers of this session by normalised query: backspacing or retyping costs no request.
	private readonly Dictionary<string, IReadOnlyList<Location>> _cache = new(StringComparer.CurrentCultureIgnoreCase);
	private readonly Queue<string> _cacheOrder = new();

	private readonly LocationService _locations;
	private readonly LineIndex _lineIndex;
	private readonly LineCourseService _lineCourses;
	private readonly NetworkService _network;
	private readonly PlaceStore _store;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	// Lifetime rule: every search owns its source and disposes it in its own finally block.
	// Everyone else may only Cancel() it (guarded), never Dispose() it.
	private CancellationTokenSource? _search;
	private bool _targetIsFrom;
	private string? _target;
	private bool _isNavigating;
	private bool _openingLine;
	private MessageKind _messageKind;

	public PlaceSearchViewModel(
	LocationService locations,
	LineIndex lineIndex,
	LineCourseService lineCourses,
	NetworkService network,
	PlaceStore store,
	AppSettings settings,
	ProviderRegistry providers)
	{
		ArgumentNullException.ThrowIfNull(locations);
		ArgumentNullException.ThrowIfNull(network);
		ArgumentNullException.ThrowIfNull(lineIndex);
		ArgumentNullException.ThrowIfNull(lineCourses);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(providers);

		_locations = locations;
		_lineIndex = lineIndex;
		_lineCourses = lineCourses;
		_network = network;
		_store = store;
		_settings = settings;
		_providers = providers;
		_providerId = providers.SelectedId;
		_localization = LocalizationService.Current;

		ListenToLocalization(_localization, OnLocalizationChanged);
		Subscribe(
			() => _providers.SelectionChanged += OnProviderChanged,
			() => _providers.SelectionChanged -= OnProviderChanged);

		SelectPlaceCommand = new AsyncCommand<Location>(SelectPlaceAsync);

		SelectLineCommand = new AsyncCommand<LineRow>(SelectLineAsync);

		try
		{
			foreach (Location place in _store.Recents)
			{
				Recents.Add(new PlaceRow(place));
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"Recents unavailable: {ex.Message}");
		}

		SetMessage(
			Recents.Count == 0
				? MessageKind.Hint
				: MessageKind.None);
	}

	public AsyncCommand<Location> SelectPlaceCommand { get; }

	/// <summary>Shows the course of the whole line on the map (both directions when they can be told apart).</summary>
	public AsyncCommand<LineRow> SelectLineCommand { get; }

	public ObservableCollection<PlaceRow> Results { get; } = [];

	public ObservableCollection<PlaceRow> Recents { get; } = [];

	/// <summary>The line search's rows (see <see cref="UpdateLines"/>).</summary>
	public ObservableCollection<LineRow> Lines { get; } = [];

	public string Query
	{
		get;
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
		get;
		private set => SetProperty(ref field, value);
	}

	/// <summary>Hint, "nothing found" or error. Empty when there is nothing to say.</summary>
	public string Message
	{
		get;
		private set => SetProperty(ref field, value);
	} = string.Empty;

	public bool HasMessage => Message.Length > 0;
	public bool HasResults => Results.Count > 0;
	public bool HasLines => Lines.Count > 0;
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

		_target =
			query.TryGetValue(
				Routes.Target,
				out object? target)
			&& target is string name
				? name
				: null;
	}

	protected override void OnDisposing() =>
		Cancel();

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

	private void OnProviderChanged(object? sender, string providerId)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (IsDisposed)
			{
				return;
			}

			_providerId = providerId;

			_cache.Clear();
			_cacheOrder.Clear();

			CancelCurrent();
			Results.Clear();
			UpdateLines(Query.Trim());

			if (Query.Trim().Length >= MinQueryLength)
			{
				OnQueryChanged();
			}
			else
			{
				RefreshState();
			}
		});
	}

	private void OnQueryChanged()
	{
		CancelCurrent();

		string text = Query.Trim();

		UpdateLines(text);

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

		// Keep the previous results on screen until the new ones arrive,
		// so the list does not flash empty on every keystroke.
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
		string key = CacheKey(text);

		try
		{
			IReadOnlyList<Location> found;

			if (_cache.TryGetValue(key, out IReadOnlyList<Location>? cached))
			{
				found = cached;
			}
			else
			{
				// Cancelled by the next keystroke; only the last text of a burst reaches the endpoint.
				await Task.Delay(Debounce, token);

				IsSearching = true;

				found =
					await _locations.SearchAsync(
						text,
						TimeSpan.FromSeconds(_settings.TimeoutSeconds),
						Kinds,
						token);

				Remember(key, found);
			}

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
			DiagnosticLog.Write(
				$"Place search failed:\n{ex}");

			if (ReferenceEquals(_search, cts))
			{
				Results.Clear();

				if (ex is ApiException
					{
						StatusCode: null,
						Message.Length: > 0
					} provider)
				{
					// A status message of the provider itself (not an HTTP failure): shown unchanged.
					SetMessage(MessageKind.None, provider.Message);
				}
				else
				{
					// No answer or an HTTP error: localized text. The technical cause (status line, start
					// of the response) is added in technical-details mode only.
					string? detail =
						_settings.ShowTechnicalDetails
						&& ex is ApiException
						{
							Detail.Length: > 0
						} api
							? api.Detail
							: null;

					SetMessage(
						MessageKind.ServiceUnavailable,
						detail is null
							? null
							: $"{_localization.CurrentStrings.PlaceSearch.CouldNotReachService}\n{detail}");
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

	private void Remember(
	string key,
	IReadOnlyList<Location> found)
	{
		if (_cache.TryAdd(key, found))
		{
			_cacheOrder.Enqueue(key);

			while (_cacheOrder.Count > CacheSize)
			{
				_cache.Remove(_cacheOrder.Dequeue());
			}
		}
	}

	/// <summary>
	/// The line search: a short digit form ("11", "S1", "RE01") names a line as well as a place. The
	/// candidates are the lines the app has seen at the stops it loaded (nothing is asked of a provider for
	/// this), so the rows appear at once, above the place results.
	/// </summary>
	private void UpdateLines(string text)
	{
		Lines.Clear();

		if (LineIndex.IsLineQuery(text))
		{
			foreach (IndexedLine line in _lineIndex.Search(_providerId, text))
			{
				Lines.Add(new LineRow(line));
			}
		}

		OnPropertyChanged(nameof(HasLines));
	}

	/// <summary>Opens the course of the whole line, the same map the departures page's line rows open.</summary>
	private async Task SelectLineAsync(LineRow row)
	{
		if (row is null || _openingLine)
		{
			return;
		}

		_openingLine = true;

		try
		{
			IReadOnlyList<LineCourse> courses =
				await _lineCourses.BothDirectionsAsync(
					new TransitLine { Name = row.Line.Name, Mode = row.Line.Mode },
					row.Line.SeenAt,
					_settings.TimeoutSeconds);

			if (courses.Count == 0)
			{
				SetMessage(MessageKind.None, _localization.CurrentStrings.Extras.LineMapNone);
				RefreshState();

				return;
			}

			LineCourse first = courses[0];

			string title =
				string.Format(
					CultureInfo.CurrentCulture,
					_localization.CurrentStrings.Extras.LineCourseTitle,
					first.LineName,
					first.FirstTerminus ?? string.Empty,
					first.LastTerminus ?? string.Empty);

			// B4: the diversions note of the line rides along (active route changes; failures stay quiet).
			string? notice = await LineCourseNotices.OfLineAsync(_network, first.LineName);

			await MapScenes.OpenAsync(
				MapScenes.FromLineCourses(courses),
				title,
				notice: notice);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Place search] line course for {row.Line.Name} failed: {ex.Message}");
		}
		finally
		{
			_openingLine = false;
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
				// A default start is a setting, not a place the passenger went to.
				if (_target is not (Routes.TargetStart or Routes.TargetHome))
				{
					_store.AddRecent(place);
				}
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write(
					$"Remembering the place failed: {ex.Message}");
			}

			await Shell.Current.GoToAsync(
				"..",
				new ShellNavigationQueryParameters
				{
					[Routes.SelectedPlace] = place,
					[Routes.TargetIsFrom] = _targetIsFrom,
					[Routes.Target] = _target ?? string.Empty
				});
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
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
			if (IsDisposed)
			{
				return;
			}

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
				_localization.CurrentStrings.PlaceSearch.TypeAtLeastThreeCharacters,

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