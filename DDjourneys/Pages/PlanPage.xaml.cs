using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class PlanPage : ContentPage, IQueryAttributable
{
	private readonly PlanViewModel _vm;

	public PlanPage(PlanViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;

		vm.OpenPlaceSearch = isFrom => Shell.Current.GoToAsync(
			Routes.PlaceSearch,
			new Dictionary<string, object>
			{
				[Routes.TargetIsFrom] = isFrom
			});

		// Temporary until the Results page exists.
		vm.OpenResults = query => DisplayAlertAsync(
			"Search",
			$"{query.From} to {query.To}\n"
			+ $"{(query.SearchMode == Core.Models.JourneySearchMode.Arrival ? "Arrive by" : "Depart")} "
			+ $"{query.DateTime:ddd d MMM}, {Format.Time(query.DateTime)}",
			"OK");
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (!query.TryGetValue(Routes.SelectedPlace, out object? selectedPlace))
		{
			return;
		}

		if (selectedPlace is not Location place
			|| !query.TryGetValue(Routes.TargetIsFrom, out object? targetIsFrom)
			|| targetIsFrom is not bool isFrom)
		{
			throw new InvalidOperationException("Place search returned invalid navigation data.");
		}

		if (isFrom)
		{
			_vm.From = place;
		}
		else
		{
			_vm.To = place;
		}
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
