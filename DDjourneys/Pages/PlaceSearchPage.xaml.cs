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
		QueryEntry.Focus();
	}

	private void PlaceTapped(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is PlaceRow row)
		{
			_vm.SelectPlaceCommand.Execute(row.Place);
		}
	}
}