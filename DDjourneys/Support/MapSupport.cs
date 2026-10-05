using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>Why the map is not drawn on this device.</summary>
public enum MapBlock
{
	/// <summary>Nothing: the map may be drawn.</summary>
	None,

	/// <summary>The device or its web view cannot run MapLibre GL (WebGL 2 and a current JavaScript engine).</summary>
	Device,

	/// <summary>The last attempt to show the map ended the app; the map stays off until the key is changed.</summary>
	Crashed
}

/// <summary>
/// Whether this device can draw the map at all. MapLibre GL 5 needs WebGL 2 and a Chromium of the last few years; on
/// an older web view (an Android 7 emulator with OpenGL ES 2.0, say) loading the script can end the whole app, because
/// the web view runs inside the app's process. So the page is not even loaded when the device is known to fail, and a
/// load that did end the app is remembered and not repeated (<see cref="MapBlock.Crashed"/>).
/// </summary>
public static class MapSupport
{
	/// <summary>Smallest Chromium major version of the web view that runs MapLibre GL 5 (optional chaining arrived in 80).</summary>
	public const int MinimumChromium = 80;

	private const string PendingKey = "map.pending";
	private const string CrashedKey = "map.crashed";

	private static (MapBlock Block, string Detail)? _check;
	private static string? _pageBlock;

	public static MapBlock Block =>
		_pageBlock is not null
			? MapBlock.Device
			: (_check ??= Evaluate()).Block;

	/// <summary>The technical reason, for the log.</summary>
	public static string Detail =>
		_pageBlock ?? (_check ??= Evaluate()).Detail;

	/// <summary>The page is about to be loaded: if the app ends before <see cref="Finish"/>, the next start knows.</summary>
	public static void Begin() =>
		Store(PendingKey, true);

	/// <summary>The page answered (ready or unsupported), or was closed in an orderly way.</summary>
	public static void Finish() =>
		Store(PendingKey, false);

	/// <summary>The user changed the key: one more try.</summary>
	public static void Retry()
	{
		Store(CrashedKey, false);
		Store(PendingKey, false);

		_pageBlock = null;
		_check = null;
	}

	/// <summary>The page itself found it cannot draw (WebGL 2 missing and the like).</summary>
	public static void ReportFromPage(string reason)
	{
		_pageBlock = reason;

		DiagnosticLog.Write($"[Map] the page cannot draw the map: {reason}");
	}

	private static (MapBlock, string) Evaluate()
	{
		try
		{
			if (Preferences.Default.Get(CrashedKey, false)
				|| Preferences.Default.Get(PendingKey, false))
			{
				Store(CrashedKey, true);
				Store(PendingKey, false);

				DiagnosticLog.Write("[Map] the previous attempt to show the map ended the app; the map stays off until the key is changed");

				return (MapBlock.Crashed, "previous attempt did not finish");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] crash marker unreadable: {ex.Message}");
		}

		(bool ok, string detail) = Inspect();

		DiagnosticLog.Write($"[Map] device check: {(ok ? "ok" : "NOT supported")} ({detail})");

		return (ok ? MapBlock.None : MapBlock.Device, detail);
	}

	private static (bool Ok, string Detail) Inspect()
	{
#if ANDROID
		try
		{
			int api = (int)global::Android.OS.Build.VERSION.SdkInt;
			int glMajor = 0;

			if (global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.ActivityService) is global::Android.App.ActivityManager manager
				&& manager.DeviceConfigurationInfo is { } info)
			{
				glMajor = info.ReqGlEsVersion >> 16;
			}

			int chromium = InstalledChromiumMajor();
			string summary = $"API {api}, OpenGL ES {glMajor}.x, web view Chromium {(chromium > 0 ? chromium.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown")}";

			// The device reports the highest OpenGL ES it offers; WebGL 2 needs ES 3.0. 0 = could not be read: do not block.
			if (glMajor is > 0 and < 3)
			{
				return (false, $"{summary}: WebGL 2 needs OpenGL ES 3.0");
			}

			if (chromium is > 0 and < MinimumChromium)
			{
				return (false, $"{summary}: Chromium {MinimumChromium} or newer is needed");
			}

			return (true, summary);
		}
		catch (Exception ex)
		{
			return (true, $"not checked: {ex.Message}");
		}
#else
		return (true, "desktop web view");
#endif
	}

#if ANDROID
	private static int InstalledChromiumMajor()
	{
		string? version = null;

		if (OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			version = global::Android.Webkit.WebView.CurrentWebViewPackage?.VersionName;
		}

		if (version is null)
		{
			foreach (string package in (string[])["com.google.android.webview", "com.android.webview", "com.android.chrome"])
			{
				try
				{
					version =
						global::Android.App.Application.Context.PackageManager?.GetPackageInfo(package, (global::Android.Content.PM.PackageInfoFlags)0)?.VersionName;
				}
				catch (Exception)
				{
					// Not installed.
				}

				if (version is not null)
				{
					break;
				}
			}
		}

		return
			version is { Length: > 0 }
			&& int.TryParse(version.AsSpan(0, version.IndexOf('.') is var dot and > 0 ? dot : version.Length), out int major)
				? major
				: 0;
	}
#endif

	private static void Store(string key, bool value)
	{
		try
		{
			Preferences.Default.Set(key, value);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] marker '{key}' not stored: {ex.Message}");
		}
	}
}
