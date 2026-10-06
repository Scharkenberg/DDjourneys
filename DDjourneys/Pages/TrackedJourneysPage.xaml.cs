using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public partial class TrackedJourneysPage : ContentPage
{
	private static readonly TimeSpan ScrollDelay = TimeSpan.FromMilliseconds(350);
	private static readonly int MaxScrollAttempts = 20;

	private readonly TrackedJourneysViewModel _vm;

	private string? _scrollTarget;

	/// <summary>Cards that have come in already; the rows are rebuilt on every refresh and must not replay it.</summary>
	private readonly HashSet<string> _revealed = [];

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

	private void CardLoaded(object? sender, EventArgs e)
	{
		if (!Motion.Enabled
			|| sender is not Border card
			|| card.BindingContext is not TrackedRow row
			|| !_revealed.Add(row.PlanId))
		{
			return;
		}

		_ = Motion.RevealAsync(card, Math.Min(_revealed.Count - 1, 8) * 55, 300, 16);
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

		_ = ScrollToTargetAsync(MaxScrollAttempts);
	}

	private async Task ScrollToTargetAsync(int attemptsLeft)
	{
		string? target = _scrollTarget;

		if (target is null)
		{
			return;
		}

		if (!IsLoaded)
		{
			if (attemptsLeft > 0)
			{
				await Task.Delay(ScrollDelay);
				await ScrollToTargetAsync(attemptsLeft - 1);
			}

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
				if (attemptsLeft > 0)
				{
					await Task.Delay(ScrollDelay);
					await ScrollToTargetAsync(attemptsLeft - 1);
				}

				return;
			}

			_scrollTarget = null;
			await Scroller.ScrollToAsync(card, ScrollToPosition.Start, Motion.Enabled);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Scrolling to the followed journey failed: {ex.Message}");
		}
	}
}
