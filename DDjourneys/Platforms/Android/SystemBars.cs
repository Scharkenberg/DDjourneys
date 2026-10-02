using Android.App;
using AndroidX.Core.View;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Status and navigation bar: fully transparent (the page background shows through, so they follow
/// every app theme), with icon colours that stay legible on the current palette.
/// </summary>
internal static class SystemBars
{
	public static void Apply(Activity? activity, bool isDark)
	{
		if (activity?.Window is not { } window)
		{
			return;
		}

		try
		{
#pragma warning disable CA1422, CS0618 // setStatusBarColor is deprecated from API 35 but still needed below it
			window.SetStatusBarColor(global::Android.Graphics.Color.Transparent);
			window.SetNavigationBarColor(global::Android.Graphics.Color.Transparent);
#pragma warning restore CA1422, CS0618

			if (OperatingSystem.IsAndroidVersionAtLeast(29))
			{
				// No grey scrim behind transparent system bars.
				window.NavigationBarContrastEnforced = false;
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
