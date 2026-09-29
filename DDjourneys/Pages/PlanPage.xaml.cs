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
			new ShellNavigationQueryParameters
			{
				[Routes.TargetIsFrom] = isFrom
			});

		vm.OpenResults = query => Shell.Current.GoToAsync(
			Routes.Results,
			new ShellNavigationQueryParameters
			{
				[Routes.Query] = query
			});
	}

	/// <summary>Receives the place chosen on the place search page.</summary>
	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.SelectedPlace, out object? chosen) && chosen is Location place
			&& query.TryGetValue(Routes.TargetIsFrom, out object? target) && target is bool isFrom)
		{
			if (isFrom)
			{
				_vm.From = place;
			}
			else
			{
				_vm.To = place;
			}
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
	private async void FromTapped(object? sender, TappedEventArgs e)
	{
		await OpenPlaceSearchAsync(true);
	}

	private async void ToTapped(object? sender, TappedEventArgs e)
	{
		await OpenPlaceSearchAsync(false);
	}

	private async Task OpenPlaceSearchAsync(bool isFrom)
	{
		try
		{
			await Shell.Current.GoToAsync(
				Routes.PlaceSearch,
				new ShellNavigationQueryParameters
				{
					[Routes.TargetIsFrom] = isFrom
				});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"Place-search navigation failed:\n{ex}");

			await DisplayAlertAsync(
				"Navigation error",
				$"{ex.GetType().Name}\n\n{ex.Message}",
				"OK");
		}
	}
}
