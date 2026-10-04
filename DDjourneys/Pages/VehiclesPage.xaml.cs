using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class VehiclesPage : ContentPage
{
	private readonly VehiclesViewModel _vm;
	private IDispatcherTimer? _timer;

	// Shell hands the navigation query to the BindingContext (IQueryAttributable).
	public VehiclesPage(VehiclesViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);
		BindingContext = _vm = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		if (_timer is null)
		{
			_timer = Dispatcher.CreateTimer();
			_timer.Interval = TimeSpan.FromSeconds(5);
			_timer.Tick += (_, _) => _vm.Tick();
		}

		_timer.Start();
	}

	protected override void OnDisappearing()
	{
		_timer?.Stop();
		base.OnDisappearing();
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}

	private void FilterCompleted(object? sender, EventArgs e) =>
		_vm.StartCommand.Execute(null);
}
