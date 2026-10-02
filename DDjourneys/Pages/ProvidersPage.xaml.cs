using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class ProvidersPage : ContentPage
{
	public ProvidersPage(ProvidersViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);

		BindingContext = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		Motion.EnterPage(this);
	}
}
