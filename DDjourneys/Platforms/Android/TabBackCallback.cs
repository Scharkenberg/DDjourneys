using AndroidX.Activity;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Android back (button and gesture) on the phone's tab bar: back never ends the app anywhere but the planner,
/// the app's home page. A pushed page, a pane or a modal still closes one level - the shell's own callback runs
/// first and handles all of that; this one is asked only when the shell had nothing to pop, and then the root
/// of one of the other tabs switches to the planner instead of ending the app. Registered before
/// <see cref="PaneBackCallback"/>, so on the way down the chain a pane closes before this decides.
/// </summary>
internal sealed class TabBackCallback : OnBackPressedCallback
{
	private readonly ComponentActivity _activity;

	private TabBackCallback(ComponentActivity activity)
		: base(true)
	{
		_activity = activity;
	}

	public static void Register(ComponentActivity activity)
	{
		ArgumentNullException.ThrowIfNull(activity);

		activity.OnBackPressedDispatcher.AddCallback(activity, new TabBackCallback(activity));
	}

	public override void HandleOnBackPressed()
	{
		Shell? shell = Shell.Current;

		// Without the shell, or without the phone's tabs, the default applies: pop what is there, then close.
		if (shell is null || !TabRoots.Enabled)
		{
			Forward();

			return;
		}

		try
		{
			if (shell.CurrentItem?.CurrentItem is not { } section)
			{
				Forward();

				return;
			}

			// The tab in front is at its root when nothing sits above it: no pushed page in its stack, no pane, no modal.
			bool atRoot = CountPages(section) <= 1
				&& shell.Navigation.ModalStack.Count == 0
				&& !Panes.CanGoBack;

			// Something to close first, or the planner itself: hand the press on (the shell pipeline, a pane,
			// or the activity's default, which is what ends the app).
			if (!atRoot || section.Route == Routes.Plan)
			{
				Forward();

				return;
			}

			// The root of another tab: never end the app - the planner is one step back.
			_ = TabRoots.OpenAsync(Routes.Plan);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Back on the tabs fell through to the default: {ex.Message}");

			Forward();
		}
	}

	private static int CountPages(ShellSection section)
	{
		int count = 0;

		foreach (Page? page in section.Stack)
		{
			if (page is not null)
			{
				count++;
			}
		}

		return count;
	}

	private void Forward()
	{
		Enabled = false;
		_activity.OnBackPressedDispatcher.OnBackPressed();
		Enabled = true;
	}
}
