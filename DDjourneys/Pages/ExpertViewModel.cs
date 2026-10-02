using CommunityToolkit.Maui.Alerts;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Expert view: everything the provider delivered for one journey, as readable key/value blocks.</summary>
public sealed class ExpertViewModel : ObservableObject, IQueryAttributable
{
	private readonly LocalizationService _localization =
		LocalizationService.Current;

	public ExpertViewModel()
	{
		CopyCommand =
			new AsyncCommand(
				CopyAsync);
	}

	public IReadOnlyList<ExpertSection> Sections
	{
		get => field;

		private set =>
			SetProperty(
				ref field,
				value);
	} = [];

	public AsyncCommand CopyCommand { get; }

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.JourneyData,
				out object? value)
			&& value is Journey journey)
		{
			Sections =
				ExpertReport.Build(
					journey);
		}
	}

	private async Task CopyAsync()
	{
		await Clipboard.Default.SetTextAsync(
			ExpertReport.ToText(
				Sections));

		await Toast.Make(
				_localization.CurrentStrings.Expert.Copied)
			.Show();
	}
}
