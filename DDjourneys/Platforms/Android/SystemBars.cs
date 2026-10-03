using Android.App;
using Android.Graphics.Drawables;
using AndroidX.Core.View;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Platform;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Status and navigation bar: they carry the page background of the palette in effect (never a fixed
/// colour from the native theme), with icon colours that stay legible on it. The Community Toolkit's
/// <see cref="StatusBar"/> does the status bar (it also covers the edge-to-edge window of Android 15+);
/// the window and decor background are painted too, so whatever shows behind a bar follows the palette.
/// </summary>
internal static class SystemBars
{
	public static void Apply(Activity? activity, bool isDark)
	{
		if (activity?.Window is null)
		{
			return;
		}

		if (MainThread.IsMainThread)
		{
			ApplyNow(activity, isDark);
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(() => ApplyNow(activity, isDark));
		}
	}

	private static void ApplyNow(Activity activity, bool isDark)
	{
		if (activity.Window is not { } window)
		{
			return;
		}

		Color color = Support.Theme.BarColor;
		global::Android.Graphics.Color native = color.ToPlatform();

		try
		{
			window.SetBackgroundDrawable(new ColorDrawable(native));
			window.DecorView?.SetBackgroundColor(native);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Window background styling failed: {ex.Message}");
		}

		try
		{
			StatusBar.SetColor(color);
			StatusBar.SetStyle(isDark ? StatusBarStyle.LightContent : StatusBarStyle.DarkContent);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Status bar styling failed: {ex.Message}");
		}

		try
		{
#pragma warning disable CA1422, CS0618 // set*BarColor is deprecated from API 35 (edge-to-edge) but still needed below it
			window.SetStatusBarColor(native);
			window.SetNavigationBarColor(native);
#pragma warning restore CA1422, CS0618

			if (OperatingSystem.IsAndroidVersionAtLeast(29))
			{
				window.NavigationBarContrastEnforced = false;
				window.StatusBarContrastEnforced = false;
			}

			if (window.DecorView is { } decor
				&& WindowCompat.GetInsetsController(window, decor) is { } controller)
			{
				controller.AppearanceLightStatusBars = !isDark;
				controller.AppearanceLightNavigationBars = !isDark;
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"System bars styling failed: {ex.Message}");
		}
	}
}
