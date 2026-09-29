using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class ResultsPage : ContentPage
{
	private readonly ResultsViewModel _vm;

	public ResultsPage(ResultsViewModel vm)
	{
		InitializeComponent();
		BindingContext = _vm = vm;

		vm.OpenJourney = journey => Shell.Current.GoToAsync(
			Routes.Journey,
			new ShellNavigationQueryParameters
			{
				[Routes.JourneyData] = journey
			});
	}

	private void JourneyTapped(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is JourneyCardModel model)
		{
			_vm.OpenJourneyCommand.Execute(model.Journey);
		}
	}
}
