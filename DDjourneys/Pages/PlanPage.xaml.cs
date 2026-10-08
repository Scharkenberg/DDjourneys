using DDjourneys.Core.Diagnostics;
using DDjourneys.Contract;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class PlanPage : PanePage, IQueryAttributable
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

		vm.OpenViaSearch = () =>
			NavigateAsync(
				Routes.PlaceSearch,
				new ShellNavigationQueryParameters
				{
					[Routes.TargetIsFrom] = false,
					[Routes.Target] = Routes.TargetVia
				});

		vm.OpenHomeSearch = () =>
			NavigateAsync(
				Routes.PlaceSearch,
				new ShellNavigationQueryParameters
				{
					[Routes.TargetIsFrom] = false,
					[Routes.Target] = Routes.TargetHome
				});

		vm.OpenResults = query =>
			NavigateAsync(
				Routes.Results,
				new ShellNavigationQueryParameters
				{
					[Routes.Query] = query
				});

		// The names scroll sideways; on Android the scroller keeps the tap from the row, so it is forwarded.
		FromStop.Tapped += (_, _) => FromTapped(FromStop, new TappedEventArgs(null));
		ToStop.Tapped += (_, _) => ToTapped(ToStop, new TappedEventArgs(null));
		ViaStop.Tapped += (_, _) => vm.PickViaCommand.Execute(null);

		vm.ShowError = message =>
			DisplayAlertAsync(
				_localization.CurrentStrings.Common.SomethingWentWrong,
				message,
				_localization.CurrentStrings.Common.Ok);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		_vm.Refresh();
		Motion.EnterPage(this);

		if (!_startHandled)
		{
			_startHandled = true;

			_ = StartInputModeAsync();
		}
	}

	/// <summary>Once per start of the app, not each time the planner shows again.</summary>
	private static bool _startHandled;

	/// <summary>
	/// The setting "open in input mode". A request from outside (a link, a quick action, a notification) comes first:
	/// it brings its own destination.
	/// </summary>
	private async Task StartInputModeAsync()
	{
		if (!_settings.StartInput)
		{
			return;
		}

		// Let an early request from outside arrive before deciding.
		await Task.Delay(500);

		if (ContractEntry.HasPending || AppShortcuts.IsBusy)
		{
			return;
		}

		await _vm.StartInputModeAsync();
	}

	/// <summary>An external request (contract): fill the planner and, when asked, search.</summary>
	/// <summary>The quick action "take me home".</summary>
	public Task TakeMeHomeAsync() =>
		_vm.TakeMeHomeAsync();

	private void FromMapClicked(object? sender, EventArgs e) =>
		_ = OpenMapAsync(true);

	private void ToMapClicked(object? sender, EventArgs e) =>
		_ = OpenMapAsync(false);

	/// <summary>The map in pick mode: the tapped stop or point answers like the place search.</summary>
	private Task OpenMapAsync(bool isFrom) =>
		NavigateAsync(
			Routes.Map,
			new ShellNavigationQueryParameters
			{
				[Routes.MapMode] = Routes.MapModePick,
				[Routes.TargetIsFrom] = isFrom
			});

	/// <summary>After a hand-over with one end open: asks for the start / the destination.</summary>
	public void PickStart() =>
		_vm.PickFromCommand.Execute(null);

	public void PickDestination() =>
		_vm.PickToCommand.Execute(null);

	public void ApplyContract(ResolvedPlan plan)
	{
		_vm.ApplyContract(plan);

		if (plan.Search)
		{
			_vm.SearchCommand.Execute(null);
		}
		else if (plan.AskMissing)
		{
			// The short form could not fill an end (no position, no home): the planner asks for it.
			if (plan.From is null)
			{
				PickStart();
			}
			else if (plan.To is null)
			{
				PickDestination();
			}
		}
	}

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(
				Routes.SelectedPlace,
				out object? picked)
			&& picked is Location stopOver
			&& query.TryGetValue(
				Routes.Target,
				out object? purpose)
			&& purpose is string name
			&& name == Routes.TargetVia)
		{
			_vm.Via = stopOver;

			return;
		}

		if (query.TryGetValue(Routes.SelectedPlace, out object? homePick)
			&& homePick is Location home
			&& query.TryGetValue(Routes.Target, out object? homePurpose)
			&& homePurpose is string homeName
			&& homeName == Routes.TargetHome)
		{
			_vm.SetHome(home);

			return;
		}

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

	private async void SettingsClicked(
		object? sender,
		EventArgs e) =>
		await NavigateAsync(
			Routes.Settings,
			[]);

	private async void DeparturesClicked(
		object? sender,
		EventArgs e) =>
		await NavigateAsync(
			Routes.Departures,
			[]);

	private async void DisruptionsClicked(
		object? sender,
		EventArgs e) =>
		await NavigateAsync(
			Routes.Disruptions,
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
			DiagnosticLog.Write(
				$"Swap animation skipped: {ex.Message}");
		}
	}

	private void PlaceTapped(
		object? sender,
		TappedEventArgs e)
	{
		if (sender is BindableObject { BindingContext: PlaceChip { Place: var place } })
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
}