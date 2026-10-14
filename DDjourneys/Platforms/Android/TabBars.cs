using Android.App;
using Android.Content.Res;
using Android.Views;
using AndroidX.Activity;
using DDjourneys.Core.Diagnostics;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// The phone's bottom tab bar. Its colours follow the palette through the shell appearance like every other
/// surface (the Shell style in Controls.xaml; the app's own <see cref="Support.Theme"/> writes those resources),
/// with one exception: Material's pill behind the active tab's icon, which no Shell property reaches. This
/// colours it with the palette's soft accent - the fill of a selected chip - and follows every theme change.
/// </summary>
internal static class TabBars
{
	public static void Watch(ComponentActivity activity)
	{
		ArgumentNullException.ThrowIfNull(activity);

		void Follow(object? sender, EventArgs e) => Apply(activity);

		Support.Theme.Changed += Follow;

		// The activity can be recreated: the old follower stops listening.
		activity.Lifecycle.AddObserver(new Ending(() => Support.Theme.Changed -= Follow));
	}

	/// <summary>
	/// Colours the pill behind the active tab's icon. Safe to call at any time: while the shell's bottom bar does
	/// not exist yet (or on a form factor without tabs), this does nothing.
	/// </summary>
	public static void Apply(Activity? activity)
	{
		if (!MainThread.IsMainThread)
		{
			MainThread.BeginInvokeOnMainThread(() => Apply(activity));

			return;
		}

		try
		{
			if (Find(activity?.Window?.DecorView) is not { } bar)
			{
				return;
			}

			Color pill = Support.Theme.ColorOf("AccentSoft", Colors.Transparent);
			bar.ItemActiveIndicatorColor = ColorStateList.ValueOf(pill.ToPlatform());
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Tab bar styling failed: {ex.Message}");
		}
	}

	private static BottomNavigationView? Find(global::Android.Views.View? view)
	{
		if (view is BottomNavigationView bar)
		{
			return bar;
		}

		if (view is ViewGroup group)
		{
			for (int i = 0; i < group.ChildCount; i++)
			{
				if (Find(group.GetChildAt(i)) is { } found)
				{
					return found;
				}
			}
		}

		return null;
	}

	private sealed class Ending(Action end) : Java.Lang.Object, AndroidX.Lifecycle.IDefaultLifecycleObserver
	{
		public void OnDestroy(AndroidX.Lifecycle.ILifecycleOwner owner) => end();
	}
}
