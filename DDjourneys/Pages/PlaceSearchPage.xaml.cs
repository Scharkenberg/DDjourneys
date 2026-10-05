using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class PlaceSearchPage : ContentPage
{
	private readonly PlaceSearchViewModel _vm;

	// Shell hands the navigation query to the BindingContext when it is an
	// IQueryAttributable, so the page itself does not need to forward it.
	public PlaceSearchPage(PlaceSearchViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);
		BindingContext = _vm = vm;
	}

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		// The page must be attached and laid out before the field can take focus; a short delay
		// also lets the page transition finish, so the keyboard does not fight it.
		Dispatcher.DispatchDelayed(
			TimeSpan.FromMilliseconds(180),
			() => _ = ShowKeyboardAsync());
	}

	private async Task ShowKeyboardAsync()
	{
		try
		{
			if (!IsLoaded || QueryEntry.IsFocused)
			{
				return;
			}

			QueryEntry.Focus();
			await QueryEntry.ShowSoftInputAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Focus failed: {ex.Message}");
		}
	}

	/// <summary>Closes the keyboard before the page leaves, so the previous page never jumps under it.</summary>
	private void HideKeyboard()
	{
		try
		{
			QueryEntry.Unfocus();
			_ = QueryEntry.HideSoftInputAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Hiding keyboard failed: {ex.Message}");
		}
	}

	protected override bool OnBackButtonPressed()
	{
		HideKeyboard();
		return base.OnBackButtonPressed();
	}

	protected override void OnDisappearing()
	{
		HideKeyboard();
		base.OnDisappearing();
		_vm.Cancel(); // no request outlives the page
	}

	private void PlaceTapped(object? sender, TappedEventArgs e)
	{
		if (sender is VisualElement row && (sender as BindableObject)?.BindingContext is PlaceRow place)
		{
			HideKeyboard();
			_ = Motion.TapAsync(row);
			_vm.SelectPlaceCommand.Execute(place.Place);
		}
	}
}
