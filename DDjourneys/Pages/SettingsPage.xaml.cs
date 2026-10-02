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

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_vm.RefreshTheme();
		_vm.RefreshProvider();
		Motion.EnterPage(this);
	}
}