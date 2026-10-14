using AndroidX.Activity;
using DDjourneys.Support;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Android back (button and gesture) on the phone's tab bar: back ends the app only on the planner, the app's home.
/// Enabled only while the tab in front is another tab's root with nothing above it (no pushed page, no modal, no
/// pane), so predictive back knows in advance; then back switches to the planner. Otherwise it stays out of the
/// chain and MAUI's callback pops as always. Registered before <see cref="PaneBackCallback"/>, which is asked first.
/// </summary>
internal sealed class TabBackCallback : OnBackPressedCallback
{
	private readonly ComponentActivity _activity;

	private TabBackCallback(ComponentActivity activity)
		: base(false)
	{
		_activity = activity;
	}

	public static void Register(ComponentActivity activity)
	{
		ArgumentNullException.ThrowIfNull(activity);

		if (!TabRoots.Enabled)
		{
			return;
		}

		var callback = new TabBackCallback(activity);

		void Follow(object? sender, EventArgs e) =>
			callback.Enabled = ShouldSwitch();

		// Raised by every shell navigation and every pane change (see AppShell, PanePage).
		Panes.BackChanged += Follow;
		activity.OnBackPressedDispatcher.AddCallback(activity, callback);
		activity.Lifecycle.AddObserver(new LifecycleEnd(() => Panes.BackChanged -= Follow));
	}

	public override void HandleOnBackPressed()
	{
		Enabled = false;

		if (ShouldSwitch())
		{
			_ = TabRoots.OpenAsync(Routes.Plan);

			return;
		}

		// Something came on top without a navigation event (a modal pushed directly): MAUI pops it.
		_activity.OnBackPressedDispatcher.OnBackPressed();
		Enabled = ShouldSwitch();
	}

	// The section's stack starts as { null } for its root, so a root alone counts 1.
	private static bool ShouldSwitch() =>
		Shell.Current is { } shell
		&& shell.CurrentItem?.CurrentItem is { } section
		&& section.Route != Routes.Plan
		&& section.Stack.Count <= 1
		&& shell.Navigation.ModalStack.Count == 0
		&& !Panes.CanGoBack;
}
