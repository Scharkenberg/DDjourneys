using Android.Content;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>
/// Handles the buttons and the swipe of the live notification. The broadcast may start a fresh
/// process, so the tracker is created through the application's service provider when needed, and the
/// work runs under <see cref="BroadcastReceiver.GoAsync"/> so the system does not end it early.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
internal sealed class JourneyNotificationActionReceiver : BroadcastReceiver
{
	public override void OnReceive(Context? context, Intent? intent)
	{
		string? action = intent?.Action;
		string? planId = intent?.GetStringExtra(LiveJourneyNotification.ExtraPlanId);

		if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(planId))
		{
			return;
		}

		PendingResult? pending = GoAsync();

		_ = Task.Run(
			async () =>
			{
				try
				{
					IServiceProvider? services = IPlatformApplication.Current?.Services;

					// A fresh process has no tracker yet: resolving it attaches it to the bridge.
					_ = services?.GetService<IJourneyTracker>();

					if (services?.GetService<SchutzengelCallbackBridge>() is { } bridge)
					{
						await bridge.HandleNotificationActionAsync(action, planId).ConfigureAwait(false);
					}
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[SCHUTZENGEL] Notification action failed: {ex.Message}");
				}
				finally
				{
					pending?.Finish();
				}
			});
	}
}
