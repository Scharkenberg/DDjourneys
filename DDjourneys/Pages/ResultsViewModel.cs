using System.Collections.ObjectModel;
using System.Diagnostics;
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

		RefreshCommand = new Command(() => _ = LoadAsync());
		OpenJourneyCommand = new Command<Journey>(async journey =>
		{
			if (OpenJourney is not null)
			{
				await OpenJourney(journey);
			}
		});
	}

	// Filled in by the page (later: navigation to the journey page).
	public Func<Journey, Task>? OpenJourney { get; set; }

	public Command RefreshCommand { get; }
	public Command<Journey> OpenJourneyCommand { get; }

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
		private set => SetProperty(ref field, value);
	} = string.Empty;

	/// <summary>Two-way with RefreshView; also drives the initial load indicator.</summary>
	public bool IsRefreshing
	{
		get => field;
		set => SetProperty(ref field, value);
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.Query, out object? value) && value is JourneyQuery journeyQuery)
		{
			_query = journeyQuery;
			RouteText = $"{journeyQuery.From.Name} \u2192 {journeyQuery.To.Name}";
			WhenText = DescribeWhen(journeyQuery);
			_ = LoadAsync();
		}
	}

	/// <summary>
	/// Newest load wins. Never throws: it is started fire-and-forget.
	/// The awaits resume on the UI thread, so the collection may be changed here.
	/// </summary>
	private async Task LoadAsync()
	{
		if (_query is null)
		{
			return;
		}

		_load?.Cancel();
		var cts = _load = new CancellationTokenSource();

		IsRefreshing = true;
		StatusText = string.Empty;

		try
		{
			JourneyResult result = await _journeys.SearchAsync(_query, cts.Token);

			if (cts.IsCancellationRequested)
			{
				return;
			}

			Items.Clear();

			if (result.IsSuccessful)
			{
				foreach (Journey journey in result.Journeys)
				{
					Items.Add(new JourneyCardModel(journey));
				}

				StatusText = Items.Count == 0 ? "No journeys found for this time." : string.Empty;
			}
			else
			{
				StatusText = $"{result.ErrorMessage ?? "The search failed."} Pull down to try again.";
			}
		}
		catch (OperationCanceledException)
		{
			// Superseded by a newer load.
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"Journey search failed: {ex}");

			if (!cts.IsCancellationRequested)
			{
				Items.Clear();
				StatusText = "Could not reach the timetable service. Pull down to try again.";
			}
		}
		finally
		{
			if (ReferenceEquals(_load, cts))
			{
				IsRefreshing = false;
			}
		}
	}

	private static string DescribeWhen(JourneyQuery query)
	{
		DateTime day = query.DateTime.LocalDateTime.Date;

		string dayText = day == DateTime.Today ? "today"
			: day == DateTime.Today.AddDays(1) ? "tomorrow"
			: query.DateTime.ToString("ddd d MMM");

		string mode = query.SearchMode == JourneySearchMode.Arrival ? "Arrive by" : "Depart";

		return $"{mode} {Format.Time(query.DateTime)}, {dayText}";
	}
}
