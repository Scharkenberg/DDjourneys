using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class ExpertPage : ContentPage
{
	public ExpertPage(ExpertViewModel vm)
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
