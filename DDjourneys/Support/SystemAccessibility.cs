using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// The operating system's accessibility settings the app follows (best effort, never throws): text size,
/// "remove animations", high contrast and a running screen reader. Read on demand; call
/// <see cref="Refresh"/> when the app comes back to the foreground to pick up changes.
/// </summary>
public static class SystemAccessibility
{
	/// <summary>The OS text size factor (1 = default).</summary>
	public static double TextScale { get; private set; } = 1;

	/// <summary>The OS asks for no (or reduced) animation.</summary>
	public static bool ReduceMotion { get; private set; }

	/// <summary>A high-contrast theme is active.</summary>
	public static bool HighContrast { get; private set; }

	/// <summary>A screen reader (TalkBack, Narrator) is running.</summary>
	public static bool ScreenReader { get; private set; }

	/// <summary>Raised after <see cref="Refresh"/> found a changed value.</summary>
	public static event EventHandler? Changed;

	public static void Refresh()
	{
		double scale = TextScale;
		bool motion = ReduceMotion;
		bool contrast = HighContrast;
		bool reader = ScreenReader;

		try
		{
#if ANDROID
			Android.Content.Context? context = Android.App.Application.Context;

			scale = context.Resources?.Configuration?.FontScale ?? 1;

			motion =
				Android.Provider.Settings.Global.GetFloat(
					context.ContentResolver,
					Android.Provider.Settings.Global.AnimatorDurationScale,
					1f) == 0f;

			if (context.GetSystemService(Android.Content.Context.AccessibilityService)
				is Android.Views.Accessibility.AccessibilityManager manager)
			{
				reader = manager.IsEnabled && manager.IsTouchExplorationEnabled;
			}
#elif WINDOWS
			var ui = new global::Windows.UI.ViewManagement.UISettings();

			scale = ui.TextScaleFactor;
			motion = !ui.AnimationsEnabled;

			var access = new global::Windows.UI.ViewManagement.AccessibilitySettings();

			contrast = access.HighContrast;
#endif
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Accessibility settings unreadable: {ex.Message}");
		}

		scale = Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1, 0.8, 3);

		if (scale == TextScale && motion == ReduceMotion && contrast == HighContrast && reader == ScreenReader)
		{
			return;
		}

		TextScale = scale;
		ReduceMotion = motion;
		HighContrast = contrast;
		ScreenReader = reader;

		Changed?.Invoke(null, EventArgs.Empty);
	}
}
