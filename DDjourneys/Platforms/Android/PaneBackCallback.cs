using AndroidX.Activity;
using DDjourneys.Support;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Android back (button and gesture, predictive back included) closes the deepest pane while the page in front shows
/// panes. AndroidX asks the newest enabled callback first, so this one is consulted before MAUI's own; it is enabled
/// only while there is a pane to close (<see cref="Panes.CanGoBack"/>), which predictive back needs to know in advance.
/// Otherwise back goes to MAUI and Shell as always.
/// </summary>
internal sealed class PaneBackCallback : OnBackPressedCallback
{
	private readonly ComponentActivity _activity;

	private PaneBackCallback(ComponentActivity activity)
		: base(false)
	{
		_activity = activity;
	}

	public static void Register(ComponentActivity activity)
	{
		var callback = new PaneBackCallback(activity);

		void Follow(object? sender, EventArgs e) =>
			callback.Enabled = Panes.CanGoBack;

		Panes.BackChanged += Follow;
		activity.OnBackPressedDispatcher.AddCallback(activity, callback);

		// The activity can be recreated: the old callback stops following.
		activity.Lifecycle.AddObserver(new Ending(() => Panes.BackChanged -= Follow));
	}

	public override void HandleOnBackPressed()
	{
		if (Panes.GoBack())
		{
			return;
		}

		// Nothing to close after all: hand this press on to the next callback (MAUI's).
		Enabled = false;
		_activity.OnBackPressedDispatcher.OnBackPressed();
		Enabled = Panes.CanGoBack;
	}

	private sealed class Ending(Action end) : Java.Lang.Object, AndroidX.Lifecycle.IDefaultLifecycleObserver
	{
		public void OnDestroy(AndroidX.Lifecycle.ILifecycleOwner owner) =>
			end();
	}
}
