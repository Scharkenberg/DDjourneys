using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class RoutingSettingsPage : ContentPage
{
	public RoutingSettingsPage(RoutingSettingsViewModel vm)
	{
		InitializeComponent();

		BindingContext = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		Motion.EnterPage(this);
	}
}
