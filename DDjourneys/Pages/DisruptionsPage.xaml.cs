using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class DisruptionsPage : ContentPage
{
	// Shell hands the navigation query to the BindingContext (IQueryAttributable).
	public DisruptionsPage(DisruptionsViewModel vm)
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

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}
}
