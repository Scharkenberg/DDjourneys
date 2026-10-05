using DDjourneys.Core.Mapping;
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

		_vm.SceneChanged += OnSceneChanged;
		_vm.FocusRequested += OnFocusRequested;
	}

	private void OnSceneChanged(object? sender, MapScene scene) =>
		_ = Map.ShowAsync(scene);

	// The map is at the top of the page: bring it into view, then centre the vehicle.
	private async void OnFocusRequested(object? sender, string key)
	{
		await Scroller.ScrollToAsync(0, 0, true);
		await Map.FocusAsync(key);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		if (_timer is null)
		{
			_timer = Dispatcher.CreateTimer();
			_timer.Interval = TimeSpan.FromSeconds(2);
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
