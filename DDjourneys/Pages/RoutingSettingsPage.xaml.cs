using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class RoutingSettingsPage : ContentPage
{
	public RoutingSettingsPage(RoutingSettingsViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);

		BindingContext = vm;
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		Motion.EnterPage(this);
	}
}
