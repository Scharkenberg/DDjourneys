using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public sealed class ResultsViewModel : ObservableObject, IQueryAttributable
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
	private StatusKind _statusKind;

	public ResultsViewModel(JourneyService journeys)
	{
		ArgumentNullException.ThrowIfNull(journeys);

		_journeys = journeys;
		_localization = LocalizationService.Current;

		_localization.PropertyChanged +=
			OnLocalizationChanged;

		RefreshCommand =
			new Command(() => _ = LoadAsync(pulled: true));

		ReloadCommand =
			new Command(() => _ = LoadAsync(pulled: false));

		OpenJourneyCommand =
			new AsyncCommand<Journey>(
				OpenAsync,
				onError: ReportOpenFailure);

		Items.CollectionChanged +=
			(_, _) => OnPropertyChanged(nameof(ShowStatus));
	}

	public Func<Journey, Task>? OpenJourney { get; set; }
	public Func<string, Task>? ShowError { get; set; }

	public Command RefreshCommand { get; }
	public Command ReloadCommand { get; }
	public AsyncCommand<Journey> OpenJourneyCommand { get; }

	public ObservableCollection<JourneyCardModel> Items { get; } = [];

	public string RouteText
	{
		get => field;
		private set => SetProperty(ref field, value);
	} = string.Empty;

	public string WhenText
	{
		get => field;
		private set => SetProperty(ref field, value);
	} = string.Empty;

	public string StatusText
	{
		get => field;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasStatusText));
			}
		}
	} = string.Empty;

	public bool HasStatusText =>
		StatusText.Length > 0;

	public bool IsRefreshing
	{
		get => field;
		set => SetProperty(ref field, value);
	}

	public bool IsLoading
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	public bool HasError
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	public bool ShowStatus =>
		Items.Count == 0;

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.Query,
				out object? value)
			&& value is JourneyQuery journeyQuery
			&& !ReferenceEquals(_query, journeyQuery))
		{
			_query = journeyQuery;
			RouteText =
				$"{journeyQuery.From.Name} \u2192 {journeyQuery.To.Name}";
			WhenText =
				DescribeWhen(journeyQuery);

			_ = LoadAsync(pulled: false);
		}
	}

	public void Cancel()
	{
		CancellationTokenSource? running = _load;
		_load = null;

		try
		{
			running?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		IsLoading = false;
		IsRefreshing = false;
	}

	private async Task LoadAsync(bool pulled)
	{
		JourneyQuery? query = _query;

		if (query is null)
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
			_load = new CancellationTokenSource();

		Items.Clear();
		HasError = false;
		SetStatus(StatusKind.None);
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
					StatusKind.SearchServiceUnavailable,
					result.ErrorMessage);

				return;
			}

			int skipped = 0;

			foreach (Journey journey in result.Journeys)
			{
				try
				{
					Items.Add(
						new JourneyCardModel(journey));
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
				HasError = skipped > 0;

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
			if (ReferenceEquals(_load, cts))
			{
				IsLoading = false;
				IsRefreshing = false;
				_load = null;
			}

			cts.Dispose();
		}
	}

	private async Task OpenAsync(Journey journey)
	{
		if (OpenJourney is not null)
		{
			await OpenJourney(journey);
		}
	}

	private void ReportOpenFailure(Exception ex)
	{
		try
		{
			ShowError?.Invoke(ex.Message);
		}
		catch (Exception inner)
		{
			Debug.WriteLine(
				$"Reporting failed: {inner.Message}");
		}
	}

	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			if (_query is not null)
			{
				WhenText = DescribeWhen(_query);
			}

			foreach (JourneyCardModel item in Items)
			{
				item.RefreshLocalization();
			}

			RefreshStatusText();
		});
	}

	private void SetStatus(
		StatusKind kind,
		string? customText = null)
	{
		_statusKind = kind;

		StatusText = customText
			?? kind switch
			{
				StatusKind.NoConnections =>
					_localization.CurrentStrings.Results
						.NoConnections,

				StatusKind.JourneysCouldNotBeDisplayed =>
					_localization.CurrentStrings.Results
						.JourneysCouldNotBeDisplayed,

				StatusKind.SearchServiceUnavailable =>
					_localization.CurrentStrings.Results
						.SearchServiceUnavailable,

				_ =>
					string.Empty
			};
	}

	private void RefreshStatusText()
	{
		if (_statusKind == StatusKind.None)
		{
			StatusText = string.Empty;
			return;
		}

		StatusText =
			_statusKind switch
			{
				StatusKind.NoConnections =>
					_localization.CurrentStrings.Results
						.NoConnections,

				StatusKind.JourneysCouldNotBeDisplayed =>
					_localization.CurrentStrings.Results
						.JourneysCouldNotBeDisplayed,

				StatusKind.SearchServiceUnavailable =>
					_localization.CurrentStrings.Results
						.SearchServiceUnavailable,

				_ =>
					string.Empty
			};
	}

	private string DescribeWhen(JourneyQuery query)
	{
		DateTime wall =
			Format.ToWall(query.DateTime);

		string mode =
			query.SearchMode == JourneySearchMode.Arrival
				? _localization.CurrentStrings.Plan.Arrival
				: _localization.CurrentStrings.Plan.Departure;

		string day =
			Format.DayLabel(wall);

		return
			$"{mode} " +
			$"{wall.ToString("HH:mm", CultureInfo.CurrentCulture)}, " +
			day;
	}
}