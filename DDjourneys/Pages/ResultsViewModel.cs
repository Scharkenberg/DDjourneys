using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public sealed class ResultsViewModel : ObservableObject, IQueryAttributable
{
	private readonly JourneyService _journeys;

	private JourneyQuery? _query;
	private CancellationTokenSource? _load;

	public ResultsViewModel(JourneyService journeys)
	{
		ArgumentNullException.ThrowIfNull(journeys);
		_journeys = journeys;

		// Pull-to-refresh: RefreshView owns the spinner.
		RefreshCommand = new Command(() => _ = LoadAsync(pulled: true));

		// Button and retry (Windows has no pull gesture): own spinner in the status panel.
		ReloadCommand = new Command(() => _ = LoadAsync(pulled: false));

		OpenJourneyCommand = new AsyncCommand<Journey>(OpenAsync, onError: ReportOpenFailure);

		Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowStatus));
	}

	/// <summary>Filled in by the page (navigation to the journey page).</summary>
	public Func<Journey, Task>? OpenJourney { get; set; }

	/// <summary>Called when opening a journey fails, so the page can tell the user.</summary>
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

	/// <summary>Shown when the list is empty: nothing found, or an error.</summary>
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

	public bool HasStatusText => StatusText.Length > 0;

	/// <summary>Two-way with RefreshView (pull gesture only).</summary>
	public bool IsRefreshing
	{
		get => field;
		set => SetProperty(ref field, value);
	}

	/// <summary>Spinner of the status panel while the list is empty.</summary>
	public bool IsLoading
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	/// <summary>A failed search offers a retry button.</summary>
	public bool HasError
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	/// <summary>The status panel replaces the list while it is empty.</summary>
	public bool ShowStatus => Items.Count == 0;

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		// Shell may deliver the same parameters again (e.g. when the page is revisited).
		if (query.TryGetValue(Routes.Query, out object? value)
			&& value is JourneyQuery journeyQuery
			&& !ReferenceEquals(_query, journeyQuery))
		{
			_query = journeyQuery;
			RouteText = $"{journeyQuery.From.Name} \u2192 {journeyQuery.To.Name}";
			WhenText = DescribeWhen(journeyQuery);
			_ = LoadAsync(pulled: false);
		}
	}

	/// <summary>Stops a running search (page left, app going away). Safe to call any time.</summary>
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

	/// <summary>
	/// Newest load wins. Never throws: it is started fire-and-forget.
	/// The awaits resume on the UI thread, so the collection may be changed here.
	/// </summary>
	private async Task LoadAsync(bool pulled)
	{
		JourneyQuery? query = _query;

		if (query is null)
		{
			return;
		}

		// The previous load owns and disposes its own source; we only cancel it.
		try
		{
			_load?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		var cts = _load = new CancellationTokenSource();

		// The previous query may have partially populated this list. Clear at the start
		// so refresh and navigation never present results for a different request.
		Items.Clear();

		HasError = false;
		StatusText = string.Empty;
		IsLoading = true;
		IsRefreshing = pulled;

		try
		{
			JourneyResult result = await _journeys.SearchAsync(query, cts.Token);

			if (cts.IsCancellationRequested)
			{
				return;
			}

			if (!result.IsSuccessful)
			{
				HasError = true;
				StatusText = result.ErrorMessage ?? "The search failed.";
				return;
			}

			int skipped = 0;

			foreach (Journey journey in result.Journeys)
			{
				try
				{
					Items.Add(new JourneyCardModel(journey));
				}
				catch (Exception ex)
				{
					// One malformed journey must not take the whole list down.
					skipped++;
					Debug.WriteLine($"Journey card failed:\n{ex}");
				}
			}

			if (Items.Count == 0)
			{
				HasError = skipped > 0;
				StatusText = skipped > 0
					? "The journeys could not be displayed."
					: "No journeys found for this time.";
			}
		}
		catch (OperationCanceledException)
		{
			// Superseded by a newer load, or the page was left.
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"Journey search failed:\n{ex}");

			if (!cts.IsCancellationRequested)
			{
				Items.Clear();
				HasError = true;
				StatusText = "Could not reach the timetable service.";
			}
		}
		finally
		{
			if (ReferenceEquals(_load, cts))
			{
				IsLoading = false;
				IsRefreshing = false;
				_load = null; // never keep a disposed source around: Cancel() on it would throw
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
			Debug.WriteLine($"Reporting failed: {inner.Message}");
		}
	}

	private static string DescribeWhen(JourneyQuery query)
	{
		DateTime wall = Format.ToWall(query.DateTime);
		string mode = query.SearchMode == JourneySearchMode.Arrival ? "Arrive by" : "Depart";

		string day = Format.DayLabel(wall);

		if (day is "Today" or "Tomorrow")
		{
			day = day.ToLowerInvariant();
		}

		return $"{mode} {wall.ToString("HH:mm", CultureInfo.InvariantCulture)}, {day}";
	}
}
