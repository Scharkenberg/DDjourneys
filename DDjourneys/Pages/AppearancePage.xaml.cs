using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class AppearancePage : ContentPage
{
	private readonly AppearanceViewModel _vm;

	public AppearancePage(AppearanceViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);
		_vm = vm;
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
		_vm.Refresh();
		Motion.EnterPage(this);
	}
}
