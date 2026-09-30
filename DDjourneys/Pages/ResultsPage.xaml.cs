using DDjourneys.Controls;
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

		vm.ShowError = message => DisplayAlertAsync("Could not open journey", message, "OK");
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);
	}

	protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		// Popped (going back to Plan): nobody will read the answer any more.
		if (args.DestinationPage is not JourneyPage)
		{
			_vm.Cancel();
		}
	}

	/// <summary>Cards wave in once; recycled cards while scrolling never replay it.</summary>
	private void CardLoaded(object? sender, EventArgs e)
	{
		if (!Motion.Enabled || sender is not JourneyCard card || card.BindingContext is not JourneyCardModel model || model.Revealed)
		{
			return;
		}

		model.Revealed = true;
		int index = Math.Max(0, _vm.Items.IndexOf(model));
		_ = Motion.RevealAsync(card, Math.Min(index, 8) * 50, 280, 14);
	}

	private void JourneyTapped(object? sender, TappedEventArgs e)
	{
		if (sender is VisualElement card && (sender as BindableObject)?.BindingContext is JourneyCardModel model)
		{
			_ = Motion.TapAsync(card);
			_vm.OpenJourneyCommand.Execute(model.Journey);
		}
	}
}
