using DDjourneys.Controls;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class ResultsPage : ContentPage
{
	private readonly ResultsViewModel _vm;
	private readonly LocalizationService _localization;

	public ResultsPage(ResultsViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);

		_localization = LocalizationService.Current;
		BindingContext = _vm = vm;

		vm.OpenJourney = journey =>
			Shell.Current.GoToAsync(
				Routes.Journey,
				new ShellNavigationQueryParameters
				{
					[Routes.JourneyData] = journey
				});

		vm.ShowError = message =>
			DisplayAlertAsync(
				_localization.CurrentStrings.Common.CouldNotOpenJourney,
				message,
				_localization.CurrentStrings.Common.Ok);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		if (args.DestinationPage is not JourneyPage)
		{
			_vm.Cancel();
		}

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}

	private void CardLoaded(
		object? sender,
		EventArgs e)
	{
		if (!Motion.Enabled
			|| sender is not JourneyCard card
			|| card.BindingContext
				is not JourneyCardModel model
			|| model.Revealed)
		{
			return;
		}

		model.Revealed = true;

		int index =
			Math.Max(
				0,
				_vm.Items.IndexOf(model));

		_ = Motion.RevealAsync(
			card,
			Math.Min(index, 8) * 50,
			280,
			14);
	}

	private void JourneyTapped(
		object? sender,
		TappedEventArgs e)
	{
		if (sender is VisualElement card
			&& (sender as BindableObject)?.BindingContext
				is JourneyCardModel model)
		{
			_ = Motion.TapAsync(card);
			_vm.OpenJourneyCommand.Execute(model.Journey);
		}
	}
}