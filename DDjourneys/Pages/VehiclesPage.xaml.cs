using DDjourneys.Core.Mapping;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class VehiclesPage : PanePage
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

		// A new instance on a running view model (window width changed): its map gets the scene with the next tick.
		_vm.RequestScene();
	}

	protected override void OnRetired()
	{
		_vm.SceneChanged -= OnSceneChanged;
		_vm.FocusRequested -= OnFocusRequested;
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

		// A stream that was paused with the page continues now (first appearance: nothing to continue).
		_vm.ResumeStream();

		if (_timer is null)
		{
			_timer = Dispatcher.CreateTimer();
			_timer.Interval = TimeSpan.FromSeconds(2);
			_timer.Tick += (_, _) =>
			{
				if (AppVisibility.IsShown)
				{
					_vm.Tick();
				}
			};
		}

		AppVisibility.Changed += OnWindowVisibility;

		_timer.Start();
	}

	/// <summary>A hidden or minimised window has no use for positions: the stream pauses and resumes with it.</summary>
	private void OnWindowVisibility(object? sender, EventArgs e)
	{
		if (AppVisibility.IsShown)
		{
			_vm.ResumeStream();
		}
		else
		{
			_vm.PauseStream();
		}
	}

	protected override void OnDisappearing()
	{
		_timer?.Stop();
		AppVisibility.Changed -= OnWindowVisibility;

		// Positions nobody looks at cost battery: the stream pauses until the page is seen again.
		_vm.PauseStream();

		base.OnDisappearing();
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		LeaveIfGone(args);
	}

	private void FilterCompleted(object? sender, EventArgs e) =>
		_vm.StartCommand.Execute(null);
}
