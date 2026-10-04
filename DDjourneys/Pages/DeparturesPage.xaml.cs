using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class DeparturesPage : ContentPage, IQueryAttributable
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
			_vm.SetStop(opened);
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		_vm.RefreshQuickPicks();

		// Keep the board current while it is on screen; the timer stops when the page is left.
		_timer ??= CreateTimer();
		_timer.Start();

		if (_vm.HasStop)
		{
			_ = _vm.RefreshAsync(silent: true);
		}
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

	private IDispatcherTimer CreateTimer()
	{
		IDispatcherTimer timer = Dispatcher.CreateTimer();
		timer.Interval = RefreshInterval;
		timer.Tick += (_, _) =>
		{
			if (_vm.HasStop)
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
			await Shell.Current.GoToAsync(
				route,
				parameters);
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
