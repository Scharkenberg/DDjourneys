using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class StartSettingsPage : ContentPage, IQueryAttributable
{
	private readonly StartSettingsViewModel _vm;

	public StartSettingsPage(StartSettingsViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);

		BindingContext = _vm = vm;
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.SelectedPlace, out object? picked)
			&& picked is Location place
			&& query.TryGetValue(Routes.Target, out object? purpose)
			&& purpose is string name
			&& name == Routes.TargetStart)
		{
			_vm.SetPlace(place);
		}
	}

	protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		_vm.Refresh();
		Motion.EnterPage(this, cascade: true);
	}
}
