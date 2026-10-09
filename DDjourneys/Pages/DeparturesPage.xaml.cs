using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class DeparturesPage : PanePage, IQueryAttributable
{
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

	private readonly DeparturesViewModel _vm;
	private readonly LocalizationService _localization;
	private IDispatcherTimer? _timer;

	public DeparturesPage(DeparturesViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);

		_localization = LocalizationService.Current;
		BindingContext = _vm = vm;

		LiveRow.Command =
			new Command(
				() => _ = NavigateAsync(
					Routes.Vehicles,
					[]));

		// The stop name scrolls sideways; on Android the scroller keeps the tap from the row, so it is forwarded.
		StopLine.Tapped += (_, _) => vm.PickStopCommand.Execute(null);

		vm.OpenPlaceSearch = () =>
			NavigateAsync(
				Routes.PlaceSearch,
				new ShellNavigationQueryParameters
				{
					[Routes.TargetIsFrom] = false,
					[Routes.Target] = Routes.TargetDepartures
				});

		vm.OpenRun = departure =>
			NavigateAsync(
				Routes.Run,
				new ShellNavigationQueryParameters
				{
					[Routes.DepartureData] = departure
				});

		vm.OpenChanges = departure =>
			NavigateAsync(
				Routes.Disruptions,
				string.IsNullOrWhiteSpace(departure.Line.Name)
					? new ShellNavigationQueryParameters
					{
						[Routes.ChangeIds] = string.Join(',', departure.RouteChangeIds)
					}
					: new ShellNavigationQueryParameters
					{
						[Routes.LineName] = departure.Line.Name
					});
	}

	/// <summary>The map in pick mode: a tapped stop becomes the stop of the board.</summary>
	private void MapClicked(object? sender, EventArgs e) =>
		_ = NavigateAsync(
			Routes.Map,
			new ShellNavigationQueryParameters
			{
				[Routes.MapMode] = Routes.MapModePick,
				[Routes.TargetIsFrom] = false,
				[Routes.Target] = Routes.TargetDepartures
			});

	/// <summary>The quick action "departures from here": locates the device; the nearest stop becomes the stop.</summary>
	public void StartHere() =>
		_vm.LocateCommand.Execute(null);

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.SelectedPlace, out object? picked)
			&& picked is Location place
			&& query.TryGetValue(Routes.Target, out object? purpose)
			&& purpose is string name
			&& name == Routes.TargetDepartures)
		{
			_vm.SetStop(place);

			return;
		}

		if (query.TryGetValue(Routes.Stop, out object? stop)
			&& stop is Location opened)
		{
			// From a request of another app: arrivals and a time, set before the stop loads the board.
			if (query.TryGetValue(Routes.BoardArrivals, out object? arrivals)
				&& arrivals is bool showArrivals)
			{
				_vm.ApplyBoard(showArrivals, query.TryGetValue(Routes.BoardTime, out object? boardTime) ? boardTime as DateTime? : null);
			}

			_vm.SetStop(opened);
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		_vm.RefreshQuickPicks();

		AppVisibility.Changed += OnWindowVisibility;

		// Keep the board current while it is on screen; the timer stops when the page is left, and while
		// a wide window hides the board behind deeper panes of its own chain (OnOwnVisibility).
		_timer ??= CreateTimer();

		if (IsOwnVisible)
		{
			_timer.Start();
		}

		if (_vm.HasStop)
		{
			_ = _vm.RefreshAsync(silent: true);
		}
	}

	protected override void OnDisappearing()
	{
		_timer?.Stop();
		AppVisibility.Changed -= OnWindowVisibility;
		base.OnDisappearing();
	}

	/// <summary>The window came back from minimised or hidden: the board is current again at once, and the
	/// timer runs again (a hidden board or a hidden window each leave it stopped).</summary>
	private void OnWindowVisibility(object? sender, EventArgs e)
	{
		if (AppVisibility.IsShown
			&& IsOwnVisible)
		{
			_timer?.Start();

			if (_vm.HasStop)
			{
				_ = _vm.RefreshAsync(silent: true);
			}
		}
	}

	/// <summary>A wide window can hide the board behind deeper panes of the page's own chain, without the page
	/// being left: the refresh waits until the board is in view again, and catches up at once when it returns.</summary>
	protected override void OnOwnVisibility(bool shown)
	{
		if (!shown)
		{
			_timer?.Stop();

			return;
		}

		if (AppVisibility.IsShown)
		{
			_timer?.Start();

			if (_vm.HasStop)
			{
				_ = _vm.RefreshAsync(silent: true);
			}
		}
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		LeaveIfGone(args);
	}

	private IDispatcherTimer CreateTimer()
	{
		IDispatcherTimer timer = Dispatcher.CreateTimer();
		timer.Interval = RefreshInterval;
		timer.Tick += (_, _) =>
		{
			if (_vm.HasStop
				&& AppVisibility.IsShown)
			{
				_ = _vm.RefreshAsync(silent: true);
			}
		};

		return timer;
	}

	private async Task NavigateAsync(
		string route,
		ShellNavigationQueryParameters parameters)
	{
		try
		{
			// A page that may stand beside this one opens as a pane in a wide window (see Panes).
			await Panes.GoToAsync(
				route,
				parameters,
				this);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"Navigation to '{route}' failed:\n{ex}");

			await DisplayAlertAsync(
				_localization.CurrentStrings.Common.NavigationError,
				ex.Message,
				_localization.CurrentStrings.Common.Ok);
		}
	}
}
