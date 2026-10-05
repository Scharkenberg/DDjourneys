using DDjourneys.Core.Diagnostics;
using Android.App;
using Android.Content;
using DDjourneys.Core.Tracking.Live;

namespace DDjourneys.Platforms.Android.LiveJourney;

/// <summary>The Android live notification (Android 16 live update, progress notification before).</summary>
internal sealed class AndroidLiveJourneySurface : ILiveJourneySurface
{
	public bool IsSupported => true;

	public void Show(LiveJourneyContent content) =>
		LiveJourneyNotification.Show(content);

	public void Dismiss() =>
		LiveJourneyNotification.Dismiss();

	public void ShowAlert(JourneyAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		LiveJourneyNotification.ShowAlert(alert.PlanId, alert.Kind, alert.Title, alert.Text);
	}

	public void ClearAlerts(string planId) =>
		LiveJourneyNotification.ClearAlerts(planId);
}

/// <summary>A data-sync foreground service keeps polling alive with the screen off.</summary>
internal sealed class AndroidTrackingRuntime : ITrackingRuntime
{
	public bool TryKeepAlive() =>
		Schutzengel.JourneyTrackingForegroundService.TryStart();

	public void Release() =>
		Schutzengel.JourneyTrackingForegroundService.Stop();
}

/// <summary>POST_NOTIFICATIONS (Android 13+) and the user's per-app notification switch.</summary>
internal sealed class AndroidNotificationAccess : INotificationAccess
{
	public Task<bool> CanNotifyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var manager =
				global::Android.App.Application.Context.GetSystemService(Context.NotificationService)
					as NotificationManager;

			return Task.FromResult(manager?.AreNotificationsEnabled() ?? false);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[TRACKING] Notification state unreadable: {ex.Message}");

			return Task.FromResult(false);
		}
	}

	public async Task RequestAsync()
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			return;
		}

		try
		{
			await MainThread.InvokeOnMainThreadAsync(
				async () =>
				{
					if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>()
						!= PermissionStatus.Granted)
					{
						await Permissions.RequestAsync<Permissions.PostNotifications>();
					}
				}).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[TRACKING] Notification permission request failed: {ex.Message}");
		}
	}
}
