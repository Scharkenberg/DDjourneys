using DDjourneys.Contract;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;
using SavedRoute = DDjourneys.Core.Models.SavedRoute;
using Microsoft.Maui.ApplicationModel;

namespace DDjourneys.Pages;

public partial class PlanPage : ContentPage, IQueryAttributable
{
	private readonly PlanViewModel _vm;
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	public PlanPage(
		PlanViewModel vm,
		AppSettings settings)
	{
		InitializeComponent();
		Motion.Prepare(this);

		_settings = settings;
		_localization = LocalizationService.Current;
		BindingContext = _vm = vm;

		vm.OpenPlaceSearch = isFrom =>
			NavigateAsync(
				Routes.PlaceSearch,
				new ShellNavigationQueryParameters
				{
					[Routes.TargetIsFrom] = isFrom
				});

		vm.OpenResults = query =>
			NavigateAsync(
				Routes.Results,
				new ShellNavigationQueryParameters
				{
					[Routes.Query] = query
				});

		vm.ShowError = message =>
			DisplayAlertAsync(
				_localization.CurrentStrings.Common.SomethingWentWrong,
				message,
				_localization.CurrentStrings.Common.Ok);

		// Setup location permission callbacks
		vm.CheckLocationPermission = HasLocationPermission;
		vm.RequestLocationPermission = CheckAndRequestLocationPermission;

		// Setup dialog callbacks
		vm.ShowRouteNameDialog = ShowRouteNameDialog;
		vm.ShowHomeLocationNameDialog = ShowHomeLocationNameDialog;
	}

	private bool? _dateTimeStacked;

	/// <summary>
	/// Date and time sit side by side while both pickers fit their half of the row; otherwise the time
	/// moves to a row of its own. (The Windows time picker has a wide built-in minimum that cannot be
	/// narrowed safely, so the layout adapts instead of cutting it off.)
	/// </summary>
	private void OnDateTimeGridSizeChanged(object? sender, EventArgs e)
	{
		if (DateTimeGrid.Width <= 0)
		{
			return;
		}

		double required = Math.Max(
			DesiredWidth(DatePickerControl),
			DesiredWidth(TimePickerControl));

		if (required <= 0)
		{
			return;
		}

		Thickness padding = DateTimeGrid.Padding;
		double half = (DateTimeGrid.Width - padding.HorizontalThickness - DateTimeGrid.ColumnSpacing) / 2;
		bool stacked = half < required;

		if (_dateTimeStacked == stacked)
		{
			return;
		}

		_dateTimeStacked = stacked;

		if (stacked)
		{
			DateTimeGrid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
			DateTimeGrid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
			DateTimeGrid.RowSpacing = 4;
			Grid.SetRow(TimeCell, 1);
			Grid.SetColumn(TimeCell, 0);
		}
		else
		{
			DateTimeGrid.RowDefinitions = [];
			DateTimeGrid.ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];
			DateTimeGrid.RowSpacing = 0;
			Grid.SetRow(TimeCell, 0);
			Grid.SetColumn(TimeCell, 1);
		}
	}

	private static double DesiredWidth(View view) =>
		view.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;

	protected override void OnAppearing()
	{
		base.OnAppearing();

		_vm.Refresh();
		Motion.EnterPage(this);
	}

	/// <summary>An external request (contract): fill the planner and, when asked, search.</summary>
	public void ApplyContract(ResolvedPlan plan)
	{
		_vm.ApplyContract(plan);

		if (plan.Search)
		{
			_vm.SearchCommand.Execute(null);
		}
	}

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.SelectedPlace,
				out object? chosen)
			&& chosen is Location place
			&& query.TryGetValue(
				Routes.TargetIsFrom,
				out object? target)
			&& target is bool isFrom)
		{
			if (isFrom)
			{
				_vm.From = place;
			}
			else
			{
				_vm.To = place;
			}
		}
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
			System.Diagnostics.Debug.WriteLine(
				$"Navigation to '{route}' failed:\n{ex}");

			await DisplayAlertAsync(
				_localization.CurrentStrings.Common.NavigationError,
				ex.Message,
				_localization.CurrentStrings.Common.Ok);
		}
	}

	private async void SettingsClicked(
		object? sender,
		EventArgs e) =>
		await NavigateAsync(
			Routes.Settings,
			[]);

	private async void TrackedClicked(
		object? sender,
		EventArgs e) =>
		await NavigateAsync(
			Routes.Tracked,
			[]);

	private async void SwapClicked(
		object? sender,
		EventArgs e)
	{
		try
		{
			if (sender is VisualElement swap
				&& _settings.Animations)
			{
				await swap.RotateToAsync(
					swap.Rotation + 180,
					220,
					Easing.CubicOut);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"Swap animation skipped: {ex.Message}");
		}
	}

	private void PlaceTapped(
		object? sender,
		TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext
			is Location place)
		{
			if (sender is VisualElement chip)
			{
				_ = Motion.TapAsync(chip);
			}

			_vm.UsePlace(place);
		}
	}

	private void FromTapped(
		object? sender,
		TappedEventArgs e) =>
		_vm.PickFromCommand.Execute(null);

	private void ToTapped(
		object? sender,
		TappedEventArgs e) =>
		_vm.PickToCommand.Execute(null);

	private async Task CheckAndRequestLocationPermission()
	{
		PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();

		if (status != PermissionStatus.Granted)
		{
			status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
		}

		return status == PermissionStatus.Granted;
	}

	private async Task<bool> HasLocationPermission()
	{
		PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
		return status == PermissionStatus.Granted;
	}

	private async Task<string> ShowRouteNameDialog(string defaultName)
	{
		return await DisplayPromptAsync(
			_localization.CurrentStrings.Plan.SaveCurrentRoute,
			_localization.CurrentStrings.Plan.EnterRouteName,
			initialValue: defaultName);
	}

	private async Task<string> ShowHomeLocationNameDialog(string defaultName)
	{
		return await DisplayPromptAsync(
			_localization.CurrentStrings.Plan.SetHomeLocation,
			_localization.CurrentStrings.Plan.HomeLocationName,
			initialValue: defaultName);
	}