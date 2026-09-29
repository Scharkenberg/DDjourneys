using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class PlaceSearchPage : ContentPage
{
	public PlaceSearchPage(PlaceSearchViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}

	private void PlaceSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is Location place
			&& BindingContext is PlaceSearchViewModel viewModel)
		{
			viewModel.SelectPlaceCommand.Execute(place);
		}
	}
}
