using Android.Content;
using Android.Content.PM;
using Android.Locations;
using DDjourneys.Support;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>
/// The position a widget works with. A widget refreshes in the background, where only the last position the system
/// knows is available (and from Android 10 on only with the "all the time" location permission); when there is none,
/// the last position the app itself took is used.
/// </summary>
internal static class AndroidWidgetLocation
{
	private static readonly TimeSpan MaxSystemAge = TimeSpan.FromHours(6);
	private static readonly TimeSpan MaxAppAge = TimeSpan.FromDays(7);

	public static (double Latitude, double Longitude, DateTimeOffset At)? Get(Context context)
	{
		(double Latitude, double Longitude, DateTimeOffset At)? system = FromSystem(context);
		(double Latitude, double Longitude, DateTimeOffset At)? app = DeviceLocator.LastFix();

		DateTimeOffset now = DateTimeOffset.UtcNow;

		if (system is { } known && now - known.At <= MaxSystemAge
			&& (app is not { } own || known.At >= own.At))
		{
			return known;
		}

		return app is { } fix && now - fix.At <= MaxAppAge
			? fix
			: null;
	}

	/// <summary>True when the app may read the device position at all (foreground permission).</summary>
	public static bool IsAllowed(Context context) =>
		context.CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation) == Permission.Granted
		|| context.CheckSelfPermission(global::Android.Manifest.Permission.AccessCoarseLocation) == Permission.Granted;

	private static (double Latitude, double Longitude, DateTimeOffset At)? FromSystem(Context context)
	{
		try
		{
			if (!IsAllowed(context)
				|| context.GetSystemService(Context.LocationService) is not LocationManager manager)
			{
				return null;
			}

			global::Android.Locations.Location? best = null;

			foreach (string provider in manager.GetProviders(true) ?? [])
			{
				global::Android.Locations.Location? candidate = manager.GetLastKnownLocation(provider);

				if (candidate is not null && (best is null || candidate.Time > best.Time))
				{
					best = candidate;
				}
			}

			return best is null
				? null
				: (best.Latitude, best.Longitude, DateTimeOffset.FromUnixTimeMilliseconds(best.Time));
		}
		catch (Exception ex) when (ex is Java.Lang.SecurityException or Java.Lang.IllegalArgumentException)
		{
			Core.Diagnostics.DiagnosticLog.Write($"Widget location failed: {ex.Message}");

			return null;
		}
	}
}
