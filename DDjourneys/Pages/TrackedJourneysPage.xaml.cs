using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class TrackedJourneysPage : ContentPage
{
	private static readonly TimeSpan ScrollDelay = TimeSpan.FromMilliseconds(350);

	private readonly TrackedJourneysViewModel _vm;

	private string? _scrollTarget;

	public TrackedJourneysPage(TrackedJourneysViewModel vm)
	{
		InitializeComponent();
		Motion.Prepare(this);

		BindingContext = _vm = vm;

		vm.Confirm = (title, message) =>
		{
			CommonStrings common = LocalizationService.Current.CurrentStrings.Common;

			return DisplayAlertAsync(title, message, common.Ok, common.Cancel);
		};

		vm.FocusRequested += OnFocusRequested;
	}

	/// <summary>Opens and shows one followed journey (from a notification while the page is open).</summary>
	public void Focus(string? planId) =>
		_vm.Focus(planId);

	protected override void OnNavigatedFrom(
		NavigatedFromEventArgs args)
	{
		base.OnNavigatedFrom(args);

		if (PageTeardown.IsLeavingForGood(args))
		{
			_vm.FocusRequested -= OnFocusRequested;
		}

		PageTeardown.DisposeIfLeft(args, BindingContext);
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		_vm.StartObserving();
		Motion.EnterPage(this);

		ScheduleScroll();
	}

	protected override void OnDisappearing()
	{
		_vm.StopObserving();

		base.OnDisappearing();
	}

	private void OnFocusRequested(object? sender, string planId)
	{
		_scrollTarget = planId;

		ScheduleScroll();
	}

	/// <summary>The rows are rebuilt and laid out asynchronously; scroll once they exist.</summary>
	private void ScheduleScroll()
	{
		if (_scrollTarget is null)
		{
			return;
		}

		Dispatcher.DispatchDelayed(ScrollDelay, () => _ = ScrollToTargetAsync());
	}

	private async Task ScrollToTargetAsync()
	{
		string? target = _scrollTarget;

		if (target is null || !IsLoaded)
		{
			return;
		}

		try
		{
			Element? card =
				Scroller
					.GetVisualTreeDescendants()
					.OfType<Border>()
					.FirstOrDefault(border => border.ClassId == target);

			if (card is null)
			{
				return;
			}

			_scrollTarget = null;

			await Scroller.ScrollToAsync(card, ScrollToPosition.Start, Motion.Enabled);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Scrolling to the followed journey failed: {ex.Message}");
		}
	}
}
