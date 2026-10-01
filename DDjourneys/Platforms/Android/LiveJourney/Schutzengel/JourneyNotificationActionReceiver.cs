using Android.Content;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class JourneyNotificationActionReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		var operation = intent?.Action switch
		{
			"deactivate" => SchutzengelJourneyTracker.DeactivateHandler,
			"delete" => SchutzengelJourneyTracker.DeleteHandler,
			_ => null
		};
		if (operation is not null)
			_ = Task.Run(operation);
	}
}
