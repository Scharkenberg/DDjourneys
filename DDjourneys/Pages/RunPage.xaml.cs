using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class RunPage : PanePage
{
	// Shell hands the navigation query to the BindingContext (IQueryAttributable).
	public RunPage(RunViewModel vm)
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

		LeaveIfGone(args);
	}
}
