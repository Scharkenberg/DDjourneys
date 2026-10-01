using Android.App;
using Android.Content;
using Android.OS;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

[Service(Enabled = true, Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
internal sealed class JourneyTrackingForegroundService : Service
{
	public static void Start()
	{
		var context = global::Android.App.Application.Context;
		var intent = new Intent(context, typeof(JourneyTrackingForegroundService));
		if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
		else context.StartService(intent);
	}

	public static void Stop() => global::Android.App.Application.Context.StopService(new Intent(global::Android.App.Application.Context, typeof(JourneyTrackingForegroundService)));
	public override IBinder? OnBind(Intent? intent) => null;
	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		var state = new LiveJourneyState("active", TrackingPhase.Planned, 0, 1, null, null, "Monitoring journey updates", DateTimeOffset.UtcNow);
		StartForeground(7314, LiveJourneyNotification.Build(state));
		return StartCommandResult.NotSticky;
	}
}
