using System.Collections.ObjectModel;
using DDjourneys.Core.Models;
using DDjourneys.Support;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DDjourneys.Pages;

public sealed class JourneyViewModel : ObservableObject, IQueryAttributable
{
	private readonly AppSettings _settings;
	private Journey? _journey;

	public JourneyViewModel(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		_settings = settings;

		ToggleStopsCommand = new Command<LegRow>(ToggleStops);
		ShareCommand = new Command(async () => await ShareAsync());
	}

	/// <summary>Set when the journey could not be displayed; the page shows it instead of crashing.</summary>
	public string? LoadError
	{
		get => field;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasError));
			}
		}
	}

	public bool HasError => LoadError is not null;


	public Command<LegRow> ToggleStopsCommand { get; }
	public Command ShareCommand { get; }

	public ObservableCollection<TimelineRow> Rows { get; } = [];

	public JourneyCardModel? Summary
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	public string RouteText
	{
		get => field;
		private set => SetProperty(ref field, value);
	} = string.Empty;

	public string DayText
	{
		get => field;
		private set => SetProperty(ref field, value);
	} = string.Empty;

	public IReadOnlyList<NoticeRow> Notices
	{
		get => field;
		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasNotices));
			}
		}
	} = [];

	public bool HasNotices => Notices.Count > 0;

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.JourneyData, out object? value) && value is Journey journey)
		{
			Load(journey);
		}
	}

	private void Load(Journey journey)
	{
		if (ReferenceEquals(_journey, journey))
		{
			return; // Shell may deliver the same parameters twice
		}

		try
		{
			_journey = journey;
			LoadError = null;

			Summary = new JourneyCardModel(journey);
			RouteText = $"{journey.From.Name} \u2192 {journey.To.Name}";
			DayText = journey.Departure is { } departure
				? departure.DateTime.ToString("dddd, d MMMM", System.Globalization.CultureInfo.InvariantCulture)
				: string.Empty;

			// Journey-level notices that a leg or transfer already carries would show twice.
			var nested = journey.Legs.SelectMany(l => l.Notices)
				.Concat(journey.Transfers.SelectMany(t => t.Notices))
				.ToHashSet();

			var options = new TimelineOptions(
				_settings.ShowWalkingLegs,
				_settings.ExpandNotices,
				_settings.ShowTechnicalDetails);

			Notices = journey.Notices
				.Where(n => !string.IsNullOrWhiteSpace(n) && !nested.Contains(n))
				.Distinct()
				.Select(n => new NoticeRow
				{
					Text = n,
					Description = n,
					Expanded = options.ExpandNotices,
					Technical = options.Technical
				})
				.ToList();

			Rows.Clear();

			foreach (TimelineRow row in TimelineRowFactory.Build(journey, options))
			{
				Rows.Add(row);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Journey display failed:\n{ex}");
			LoadError = "This journey could not be displayed.";
		}
	}

	/// <summary>Inserts or removes a leg's intermediate stops directly below its row.</summary>
	private void ToggleStops(LegRow? leg)
	{
		try
		{
			int at = leg is null ? -1 : Rows.IndexOf(leg);

			if (leg is null || at < 0 || !leg.HasIntermediates)
			{
				return;
			}

			if (leg.IsExpanded)
			{
				// Remove only what this leg inserted, never a neighbour.
				for (int i = 0; i < leg.Intermediates.Count && at + 1 < Rows.Count && Rows[at + 1] is IntermediateRow; i++)
				{
					Rows.RemoveAt(at + 1);
				}
			}
			else
			{
				for (int i = 0; i < leg.Intermediates.Count; i++)
				{
					leg.Intermediates[i].Index = i;
					Rows.Insert(at + 1 + i, leg.Intermediates[i]);
				}
			}

			leg.IsExpanded = !leg.IsExpanded;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Toggle stops failed:\n{ex}");
		}
	}

	private async Task ShareAsync()
	{
		if (_journey is null)
		{
			return;
		}

		try
		{
			await Share.Default.RequestAsync(new ShareTextRequest
			{
				Title = "Share journey",
				Text = BuildShareText(_journey)
			});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Share failed:\n{ex}");
		}
	}

	private string BuildShareText(Journey journey)
	{
		var lines = new List<string>
		{
			RouteText,
			$"{DayText}, {Format.TimeOrDash(journey.Departure)}\u2013{Format.TimeOrDash(journey.Arrival)} ({Format.Duration(journey.Duration)})",
			string.Empty
		};

		foreach (TimelineItem item in TimelineBuilder.Build(journey))
		{
			switch (item)
			{
				case RideItem r:
					lines.Add($"{Format.TimeOrDash(r.Leg.EffectiveDeparture)} {r.Leg.Line?.ToString() ?? r.Leg.Mode.ToString()}: "
						+ $"{r.Leg.From.Name} \u2192 {r.Leg.To.Name} ({Format.TimeOrDash(r.Leg.EffectiveArrival)})");
					break;

				case WalkItem w:
					lines.Add($"Walk {Format.Duration(w.Leg.EffectiveDeparture, w.Leg.EffectiveArrival)} to {w.Leg.To.Name}");
					break;
			}
		}

		return string.Join("\n", lines);
	}
}
