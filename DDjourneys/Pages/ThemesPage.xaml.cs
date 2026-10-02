using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class ThemesPage : ContentPage
{
	private readonly ThemesViewModel _vm;

	public ThemesPage(ThemesViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);
		_vm = vm;
		BindingContext = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_vm.Refresh();
		Motion.EnterPage(this);
	}
}