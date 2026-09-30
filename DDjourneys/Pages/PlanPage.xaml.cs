using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

public partial class PlanPage : ContentPage, IQueryAttributable
{
	private readonly PlanViewModel _vm;
	private readonly AppSettings _settings;

	public PlanPage(PlanViewModel vm, AppSettings settings)
	{
		InitializeComponent();
		_settings = settings;
		BindingContext = _vm = vm;

		vm.OpenPlaceSearch = isFrom => NavigateAsync(
			Routes.PlaceSearch,
			new ShellNavigationQueryParameters { [Routes.TargetIsFrom] = isFrom });

		vm.OpenResults = query => NavigateAsync(
			Routes.Results,
			new ShellNavigationQueryParameters { [Routes.Query] = query });

		vm.ShowError = message => DisplayAlertAsync("Something went wrong", message, "OK");
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_vm.Refresh(); // new day? stale time? fixed here, not at search time
		Motion.EnterPage(this);
	}

	/// <summary>Receives the place chosen on the place search page.</summary>
	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.SelectedPlace, out object? chosen) && chosen is Location place
			&& query.TryGetValue(Routes.TargetIsFrom, out object? target) && target is bool isFrom)
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

	private async Task NavigateAsync(string route, ShellNavigationQueryParameters parameters)
	{
		try
		{
			await Shell.Current.GoToAsync(route, parameters);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Navigation to '{route}' failed:\n{ex}");
			await DisplayAlertAsync("Navigation error", ex.Message, "OK");
		}
	}

	private async void SettingsClicked(object? sender, EventArgs e) =>
		await NavigateAsync(Routes.Settings, []);

	private async void SwapClicked(object? sender, EventArgs e)
	{
		try
		{
			if (sender is VisualElement swap && _settings.Animations)
			{
				await swap.RotateToAsync(swap.Rotation + 180, 220, Easing.CubicOut);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Swap animation skipped: {ex.Message}");
		}
	}

	private void PlaceTapped(object? sender, TappedEventArgs e)
	{
		if ((sender as BindableObject)?.BindingContext is Location place)
		{
			if (sender is VisualElement chip)
			{
				_ = Motion.TapAsync(chip);
			}

			_vm.UsePlace(place);
		}
	}

	private void FromTapped(object? sender, TappedEventArgs e) => _vm.PickFromCommand.Execute(null);

	private void ToTapped(object? sender, TappedEventArgs e) => _vm.PickToCommand.Execute(null);
}
