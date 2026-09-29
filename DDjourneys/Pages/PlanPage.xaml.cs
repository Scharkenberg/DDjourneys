using DDjourneys.Core.Services;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class PlanPage : ContentPage
{
	private readonly PlanViewModel _vm;

	public PlanPage(PlanViewModel vm, LocationService locations)
	{
		InitializeComponent();
		BindingContext = _vm = vm;

		// Temporary until the place search page exists: type a query, pick a match.
		vm.PickPlace = async title =>
		{
			string? text = await DisplayPromptAsync(title, "Station or address", "Search", "Cancel");

			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}

			try
			{
				IReadOnlyList<Location> found = await locations.SearchAsync(text);
				Location[] top = found.Take(8).ToArray();

				if (top.Length == 0)
				{
					await DisplayAlertAsync(title, "No places found.", "OK");
					return null;
				}

				string[] labels = top.Select((p, i) => $"{i + 1}. {p}").ToArray();
				string? choice = await DisplayActionSheetAsync(title, "Cancel", null, labels);
				int index = choice is null ? -1 : Array.IndexOf(labels, choice);

				return index >= 0 ? top[index] : null;
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				await DisplayAlertAsync(title, "Could not reach the timetable service.", "OK");
				return null;
			}
		};

		// Temporary until the Results page exists.
		vm.OpenResults = query => DisplayAlertAsync(
			"Search",
			$"{query.From} to {query.To}\n"
			+ $"{(query.SearchMode == Core.Models.JourneySearchMode.Arrival ? "Arrive by" : "Depart")} "
			+ $"{query.DateTime:ddd d MMM}, {Format.Time(query.DateTime)}",
			"OK");
	}

	private async void SwapClicked(object? sender, EventArgs e)
	{
		if (sender is VisualElement swap)
		{
			await swap.RotateToAsync(swap.Rotation + 180, 220, Easing.CubicOut);
		}
	}

	private void PlaceTapped(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is Location place)
		{
			_vm.UsePlace(place);
		}
	}
}
