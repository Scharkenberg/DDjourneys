using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// The operating system's accent colour, or null where there is none.
/// Android 12+ (API 31): the Material You accent (system_accent1_600 for light, _200 for dark).
/// Windows: UIColorType.Accent. Other platforms and older Android: null (callers fall back).
/// </summary>
public static class SystemAccent
{
	public static Color? TryGet(bool dark)
	{
		try
		{
#if ANDROID
			if (OperatingSystem.IsAndroidVersionAtLeast(31))
			{
				var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;

				int argb = context.GetColor(
					dark
						? global::Android.Resource.Color.SystemAccent1200
						: global::Android.Resource.Color.SystemAccent1600);

				var c = new global::Android.Graphics.Color(argb);

				return Color.FromRgb(c.R, c.G, c.B);
			}
#elif WINDOWS
			var accent = new global::Windows.UI.ViewManagement.UISettings()
				.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent);

			return Color.FromRgb(accent.R, accent.G, accent.B);
#endif
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"System accent unavailable: {ex.Message}");
		}

		return null;
	}
}
