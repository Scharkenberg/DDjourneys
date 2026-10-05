using DDjourneys.Core.Diagnostics;
using Android.App;
using Android.Content;
using Android.OS;

using DDjourneys.Core.Tracking.Live;

// The namespace is part of the generated Java class name of this service; it stays as it is
// although the class no longer lives in a Schutzengel folder.
namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>
/// Keeps the process alive while a journey is active or about to start, so the polling loop of the
/// tracker keeps running with the screen off. The notification it shows is the live notification.
/// </summary>
[Service(
	Enabled = true,
	Exported = false,
	ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
internal sealed class JourneyTrackingForegroundService : Service
{
	/// <summary>False if the system refuses (for example a start from the background).</summary>
	public static bool TryStart()
	{
		try
		{
			Context context = global::Android.App.Application.Context;
			var intent = new Intent(context, typeof(JourneyTrackingForegroundService));

			if (OperatingSystem.IsAndroidVersionAtLeast(26))
			{
				context.StartForegroundService(intent);
			}
			else
			{
				context.StartService(intent);
			}

			return true;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[SCHUTZENGEL] Foreground service not started: {ex.Message}");

			return false;
		}
	}

	public static void Stop()
	{
		try
		{
			Context context = global::Android.App.Application.Context;

			context.StopService(new Intent(context, typeof(JourneyTrackingForegroundService)));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[SCHUTZENGEL] Foreground service not stopped: {ex.Message}");
		}
	}

	public override IBinder? OnBind(Intent? intent) => null;

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		Notification notification =
			LiveJourneyNotification.Last ?? LiveJourneyNotification.BuildFallback();

		if (OperatingSystem.IsAndroidVersionAtLeast(29))
		{
			StartForeground(
				LiveJourneyNotification.LiveNotificationId,
				notification,
				global::Android.Content.PM.ForegroundService.TypeDataSync);
		}
		else
		{
			StartForeground(LiveJourneyNotification.LiveNotificationId, notification);
		}

		// The tracker restarts the service when the app is opened again.
		return StartCommandResult.NotSticky;
	}

	/// <summary>Android 15 ends data-sync services after a few hours; stopping in time avoids a crash.</summary>
	[System.Runtime.Versioning.SupportedOSPlatform("android35.0")]
	public override void OnTimeout(int startId, global::Android.Content.PM.ForegroundService fgsType)
	{
		Bridge()?.ServiceStopped();

		StopSelf();
	}

	public override void OnDestroy()
	{
		Bridge()?.ServiceStopped();

		StopForeground(StopForegroundFlags.Remove);

		base.OnDestroy();
	}

	/// <summary>The injected bridge; this component is created by the system, so DI is reached by lookup.</summary>
	private static TrackingCallbackBridge? Bridge() =>
		IPlatformApplication.Current?.Services.GetService<TrackingCallbackBridge>();
}
