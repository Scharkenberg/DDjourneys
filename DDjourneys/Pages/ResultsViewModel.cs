using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public sealed class ResultsViewModel :
	ObservableObject,
	IQueryAttributable
{
	private enum StatusKind
	{
		None,
		NoConnections,
		JourneysCouldNotBeDisplayed,
		SearchServiceUnavailable
	}

	private readonly JourneyService _journeys;
	private readonly LocalizationService _localization;

	private JourneyQuery? _query;
	private CancellationTokenSource? _load;
	private CancellationTokenSource? _paging;
	private StatusKind _statusKind;
	private bool _isPaging;

	public ResultsViewModel(
		JourneyService journeys)
	{
		ArgumentNullException.ThrowIfNull(
			journeys);

		_journeys = journeys;
		_localization = LocalizationService.Current;

		_localization.PropertyChanged +=
			OnLocalizationChanged;

		RefreshCommand =
			new Command(
				() => _ = LoadAsync(pulled: true));

		ReloadCommand =
			new Command(
				() => _ = LoadAsync(pulled: false));

		PreviousCommand =
			new AsyncCommand(
				LoadPreviousAsync,
				CanLoadPrevious,
				onError: ReportContinuationFailure);

		NextCommand =
			new AsyncCommand(
				LoadNextAsync,
				CanLoadNext,
				onError: ReportContinuationFailure);

		OpenJourneyCommand =
			new AsyncCommand<Journey>(
				OpenAsync,
				onError: ReportOpenFailure);

		Items.CollectionChanged +=
			(_, _) =>
			{
				OnPropertyChanged(
					nameof(ShowStatus));

				RefreshContinuationState();
			};
	}

	public Func<Journey, Task>? OpenJourney { get; set; }

	public Func<string, Task>? ShowError { get; set; }

	public Command RefreshCommand { get; }

	public Command ReloadCommand { get; }

	public AsyncCommand PreviousCommand { get; }

	public AsyncCommand NextCommand { get; }

	public AsyncCommand<Journey> OpenJourneyCommand { get; }

	public ObservableCollection<JourneyCardModel> Items { get; } = [];


	public string RouteText
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	} = string.Empty;


	public string WhenText
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	} = string.Empty;


	public string StatusText
	{
		get => field;
		private set
		{
			if (SetProperty(
				ref field,
				value))
			{
				OnPropertyChanged(
					nameof(HasStatusText));
			}
		}
	} = string.Empty;


	public bool HasStatusText =>
		StatusText.Length > 0;


	public bool IsRefreshing
	{
		get => field;
		set => SetProperty(
			ref field,
			value);
	}


	public bool IsLoading
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	}


	public bool IsPaging
	{
		get => _isPaging;
		private set
		{
			if (_isPaging == value)
			{
				return;
			}

			_isPaging = value;

			OnPropertyChanged();

			RefreshContinuationState();
		}
	}


	public bool HasError
	{
		get => field;
		private set => SetProperty(
			ref field,
			value);
	}


	public bool ShowStatus =>
		Items.Count == 0;


	public bool CanGoPrevious =>
		!IsPaging
		&& Items.Count > 0
		&& Items[0].Journey.Context is not null;


	public bool CanGoNext =>
		!IsPaging
		&& Items.Count > 0
		&& Items[^1].Journey.Context is not null;


	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.Query,
				out object? value)
			&& value is JourneyQuery journeyQuery
			&& !ReferenceEquals(
				_query,
				journeyQuery))
		{
			_query = journeyQuery;

			RouteText =
				$"{journeyQuery.From.Name} \u2192 " +
				$"{journeyQuery.To.Name}";

			WhenText =
				DescribeWhen(
					journeyQuery);

			_ = LoadAsync(
				pulled: false);
		}
	}


	public void Cancel()
	{
		CancellationTokenSource? running =
			_load;

		_load = null;

		CancellationTokenSource? paging =
			_paging;

		_paging = null;

		try
		{
			running?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		try
		{
			paging?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		IsLoading = false;
		IsRefreshing = false;
		IsPaging = false;
	}


	private async Task LoadAsync(
		bool pulled)
	{
		JourneyQuery? query =
			_query;

		if (query is null
			|| IsPaging)
		{
			return;
		}

		try
		{
			_load?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		var cts =
			_load =
				new CancellationTokenSource();

		Items.Clear();

		HasError = false;

		SetStatus(
			StatusKind.None);

		IsLoading = true;
		IsRefreshing = pulled;

		try
		{
			JourneyResult result =
				await _journeys.SearchAsync(
					query,
					cts.Token);

			if (cts.IsCancellationRequested)
			{
				return;
			}

			if (!result.IsSuccessful)
			{
				HasError = true;

				SetStatus(
					StatusKind.SearchServiceUnavailable);

				return;
			}

			int skipped = 0;

			foreach (Journey journey
				in result.Journeys)
			{
				try
				{
					Items.Add(
						new JourneyCardModel(
							journey));
				}
				catch (Exception ex)
				{
					skipped++;

					Debug.WriteLine(
						$"Journey card failed:\n{ex}");
				}
			}

			if (Items.Count == 0)
			{
				HasError =
					skipped > 0;

				SetStatus(
					skipped > 0
						? StatusKind.JourneysCouldNotBeDisplayed
						: StatusKind.NoConnections);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			Debug.WriteLine(
				$"Journey search failed:\n{ex}");

			if (!cts.IsCancellationRequested)
			{
				Items.Clear();

				HasError = true;

				SetStatus(
					StatusKind.SearchServiceUnavailable);
			}
		}
		finally
		{
			if (ReferenceEquals(
				_load,
				cts))
			{
				IsLoading = false;
				IsRefreshing = false;
				_load = null;

				RefreshContinuationState();
			}

			cts.Dispose();
		}
	}


	private bool CanLoadPrevious() =>
		CanGoPrevious;


	private bool CanLoadNext() =>
		CanGoNext;


	private Task LoadPreviousAsync() =>
		LoadAdjacentAsync(
			previous: true);


	private Task LoadNextAsync() =>
		LoadAdjacentAsync(
			previous: false);


	private async Task LoadAdjacentAsync(
		bool previous)
	{
		JourneyQuery? query =
			_query;

		if (query is null
			|| Items.Count == 0
			|| IsPaging)
		{
			return;
		}

		Journey anchor =
			previous
				? Items[0].Journey
				: Items[^1].Journey;

		var cts =
			_paging =
				new CancellationTokenSource();

		IsPaging = true;

		HasError = false;

		try
		{
			JourneyResult result =
				previous
					? await _journeys.GetPreviousAsync(
						query,
						anchor,
						_queryMaxResults(),
						cts.Token)
					: await _journeys.GetNextAsync(
						query,
						anchor,
						_queryMaxResults(),
						cts.Token);

			if (cts.IsCancellationRequested)
			{
				return;
			}

			if (!result.IsSuccessful)
			{
				throw new InvalidOperationException(
					string.IsNullOrWhiteSpace(
						result.ErrorMessage)
						? _localization
							.CurrentStrings
							.Results
							.SearchServiceUnavailable
						: result.ErrorMessage);
			}

			var replacement =
				new List<JourneyCardModel>(
					result.Journeys.Count);

			int skipped = 0;

			foreach (Journey journey
				in result.Journeys)
			{
				try
				{
					replacement.Add(
						new JourneyCardModel(
							journey));
				}
				catch (Exception ex)
				{
					skipped++;

					Debug.WriteLine(
						$"Adjacent journey card failed:\n{ex}");
				}
			}

			Items.Clear();

			foreach (JourneyCardModel item
				in replacement)
			{
				Items.Add(item);
			}

			if (Items.Count == 0
				&& skipped > 0)
			{
				HasError = true;

				SetStatus(
					StatusKind.JourneysCouldNotBeDisplayed);
			}
			else
			{
				SetStatus(
					StatusKind.None);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		finally
		{
			if (ReferenceEquals(
				_paging,
				cts))
			{
				_paging = null;
				IsPaging = false;
			}

			cts.Dispose();
		}
	}


	private int _queryMaxResults()
	{
		return Math.Clamp(
			_query?.MaxResults ?? 5,
			1,
			10);
	}


	private void RefreshContinuationState()
	{
		OnPropertyChanged(
			nameof(CanGoPrevious));

		OnPropertyChanged(
			nameof(CanGoNext));

		PreviousCommand
			.RaiseCanExecuteChanged();

		NextCommand
			.RaiseCanExecuteChanged();
	}


	private async Task OpenAsync(
		Journey journey)
	{
		if (OpenJourney is not null)
		{
			await OpenJourney(
				journey);
		}
	}


	private void ReportOpenFailure(
		Exception ex)
	{
		try
		{
			ShowError?.Invoke(
				_localization
					.CurrentStrings
					.Common
					.CouldNotOpenJourney);
		}
		catch (Exception inner)
		{
			Debug.WriteLine(
				$"Reporting failed: {inner.Message}");
		}
	}


	private void ReportContinuationFailure(
		Exception ex)
	{
		Debug.WriteLine(
			$"Journey continuation failed:\n{ex}");

		try
		{
			ShowError?.Invoke(
				_localization
					.CurrentStrings
					.Results
					.SearchServiceUnavailable);
		}
		catch (Exception inner)
		{
			Debug.WriteLine(
				$"Reporting continuation failure failed: {inner.Message}");
		}
	}


	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (_query is not null)
				{
					WhenText =
						DescribeWhen(
							_query);
				}

				foreach (JourneyCardModel item
					in Items)
				{
					item.RefreshLocalization();
				}

				RefreshStatusText();
			});
	}


	private void SetStatus(
		StatusKind kind)
	{
		_statusKind =
			kind;

		StatusText =
			GetStatusText(
				kind);
	}


	private void RefreshStatusText()
	{
		StatusText =
			GetStatusText(
				_statusKind);
	}


	private string GetStatusText(
		StatusKind kind) =>
		kind switch
		{
			StatusKind.NoConnections =>
				_localization
					.CurrentStrings
					.Results
					.NoConnections,

			StatusKind.JourneysCouldNotBeDisplayed =>
				_localization
					.CurrentStrings
					.Results
					.JourneysCouldNotBeDisplayed,

			StatusKind.SearchServiceUnavailable =>
				_localization
					.CurrentStrings
					.Results
					.SearchServiceUnavailable,

			_ =>
				string.Empty
		};


	private string DescribeWhen(
		JourneyQuery query)
	{
		DateTime wall =
			Format.ToWall(
				query.DateTime);

		string mode =
			query.SearchMode
				== JourneySearchMode.Arrival
				? _localization
					.CurrentStrings
					.Plan
					.Arrival
				: _localization
					.CurrentStrings
					.Plan
					.Departure;

		string day =
			Format.DayLabel(
				wall);

		return
			$"{mode} " +
			$"{wall.ToString(
				"HH:mm",
				CultureInfo.CurrentCulture)}, " +
			day;
	}
}