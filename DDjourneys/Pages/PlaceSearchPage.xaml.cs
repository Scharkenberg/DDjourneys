using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class PlaceSearchPage : ContentPage
{
	private readonly PlaceSearchViewModel _vm;

	// Shell hands the navigation query to the BindingContext when it is an
	// IQueryAttributable, so the page itself does not need to forward it.
	public PlaceSearchPage(PlaceSearchViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		try
		{
			QueryEntry.Focus();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Focus failed: {ex.Message}");
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_vm.Cancel(); // no request outlives the page
	}

	private void PlaceTapped(object? sender, TappedEventArgs e)
	{
		if (sender is VisualElement row && (sender as BindableObject)?.BindingContext is PlaceRow place)
		{
			_ = Motion.TapAsync(row);
			_vm.SelectPlaceCommand.Execute(place.Place);
		}
	}
}
