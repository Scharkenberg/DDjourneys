using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class SettingsPage : ContentPage
{
	private readonly SettingsViewModel _vm;

	public SettingsPage(SettingsViewModel vm)
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
		_vm.RefreshAppearance();
		_vm.RefreshProvider();
		_vm.RefreshLog();
		Motion.EnterPage(this, cascade: true);
	}
}