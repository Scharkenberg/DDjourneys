using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class JourneyPage : ContentPage
{
	private readonly JourneyViewModel _vm;
	private IDispatcherTimer? _clock;

	public JourneyPage(JourneyViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);
		BindingContext = _vm = vm;

		// The notice badge in the card: bring the notices into view.
		vm.ScrollToNotices = () => _ = PageScroll.ScrollToAsync(NoticesBlock, ScrollToPosition.Start, true);

		vm.ChooseShareFormat = (title, cancel, options) => ShowSharePopupAsync(this, options);

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
		_vm.RefreshFromSettings();

		if (!_vm.ExpertViewEnabled)
		{
			ToolbarItems.Remove(ExpertItem);
		}
		else if (!ToolbarItems.Contains(ExpertItem))
		{
			ToolbarItems.Insert(0, ExpertItem);
		}

		_vm.StartObservingTracking();

		// "In progress" ends with the ride: the rows are asked again every few seconds while the page shows.
		if (_clock is null)
		{
			_clock = Dispatcher.CreateTimer();
			_clock.Interval = TimeSpan.FromSeconds(15);
			_clock.Tick += (_, _) => _vm.TickClock();
		}

		_clock.Start();
		_vm.TickClock();

		Motion.EnterPage(this);
	}

	protected override void OnDisappearing()
	{
		_clock?.Stop();
		_vm.StopObservingTracking();
		base.OnDisappearing();
	}

	/// <summary>
	/// Only the text column of a row slides in; the rail and node column stays put, so the
	/// timeline line never appears broken while rows stagger in.
	/// </summary>
	private void RowLoaded(object? sender, EventArgs e)
	{
		if (!Motion.Enabled || sender is not Grid grid || grid.BindingContext is not TimelineRow row)
		{
			return;
		}

		int delay = Math.Min(row.Index, 12) * 40;

		foreach (IView child in grid.Children.ToArray())
		{
			if (child is VisualElement element && Grid.GetColumn(element) != 1)
			{
				_ = Motion.RevealAsync(element, delay, 240, 8);
			}
		}
	}

	private void StopsToggled(object? sender, TappedEventArgs e)
	{
		if (sender is BindableObject { BindingContext: LegRow leg })
		{
			_vm.ToggleStopsCommand.Execute(leg);
		}
	}

	private static async Task<string?> ShowSharePopupAsync(ContentPage page, string[] options)
	{
		var popup = new JourneySharePopup(page, options[0], options[1], options[2]);

		IPopupResult<string?> result =
			await page.ShowPopupAsync<string?>(
				popup,
				new PopupOptions
				{
					CanBeDismissedByTappingOutsideOfPopup = true,
					PageOverlayColor = Colors.Black.WithAlpha(0.45f),

					// The sheet draws its own shape, stroke and surface.
					Shape = null,
					Shadow = null
				},
				CancellationToken.None);

		return result.WasDismissedByTappingOutsideOfPopup
			? null
			: result.Result;
	}
}
