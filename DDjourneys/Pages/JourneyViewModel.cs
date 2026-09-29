using System.Collections.ObjectModel;
using DDjourneys.Core.Models;
using DDjourneys.Support;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DDjourneys.Pages;

public sealed class JourneyViewModel : ObservableObject, IQueryAttributable
{
	private Journey? _journey;

	public JourneyViewModel()
	{
		ToggleStopsCommand = new Command<LegRow>(ToggleStops);
		ShareCommand = new Command(async () => await ShareAsync());
	}

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

	public IReadOnlyList<string> Notices
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
		_journey = journey;

		Summary = new JourneyCardModel(journey);
		RouteText = $"{journey.From.Name} \u2192 {journey.To.Name}";
		DayText = journey.Departure.LocalDateTime.ToString("dddd, d MMMM");
		Notices = journey.Notices.Distinct().ToList();

		Rows.Clear();

		foreach (TimelineRow row in TimelineRowFactory.Build(journey))
		{
			Rows.Add(row);
		}
	}

	/// <summary>Inserts or removes a leg's intermediate stops directly below its row.</summary>
	private void ToggleStops(LegRow? leg)
	{
		int at = leg is null ? -1 : Rows.IndexOf(leg);

		if (leg is null || at < 0 || !leg.HasIntermediates)
		{
			return;
		}

		if (leg.IsExpanded)
		{
			for (int i = 0; i < leg.Intermediates.Count; i++)
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

	private async Task ShareAsync()
	{
		if (_journey is null)
		{
			return;
		}

		await Share.Default.RequestAsync(new ShareTextRequest
		{
			Title = "Share journey",
			Text = BuildShareText(_journey)
		});
	}

	private string BuildShareText(Journey journey)
	{
		var lines = new List<string>
		{
			RouteText,
			$"{DayText}, {Format.Time(journey.Departure)}\u2013{Format.Time(journey.Arrival)} ({Format.Duration(journey.Duration)})",
			string.Empty
		};

		foreach (TimelineItem item in TimelineBuilder.Build(journey))
		{
			switch (item)
			{
				case RideItem r:
					lines.Add($"{Format.Time(r.Leg.EffectiveDeparture)} {r.Leg.Line?.ToString() ?? r.Leg.Mode.ToString()}: "
						+ $"{r.Leg.From.Name} \u2192 {r.Leg.To.Name} ({Format.Time(r.Leg.EffectiveArrival)})");
					break;

				case WalkItem w:
					lines.Add($"Walk {Format.Duration(w.Leg.EffectiveArrival - w.Leg.EffectiveDeparture)} to {w.Leg.To.Name}");
					break;
			}
		}

		return string.Join("\n", lines);
	}
}
