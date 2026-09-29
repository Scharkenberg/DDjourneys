using System.Globalization;
using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class PlaceSearchPage : ContentPage, IQueryAttributable
{
	public PlaceSearchPage(PlaceSearchViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = null; // viewModel;

		// Delay loading until page is ready
/*		Loaded += (s, e) => {
			if (BindingContext is PlaceSearchViewModel vm)
			{
				// Trigger initial load - this will populate Results
				// and the CollectionView will render AFTER the visual tree is ready
				vm.Initialize();
			}
		}; */
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (BindingContext is IQueryAttributable viewModel)
		{
			viewModel.ApplyQueryAttributes(query);
		}
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

public class InverseBoolConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		=> !(value as bool? ?? false);

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotImplementedException();
}
