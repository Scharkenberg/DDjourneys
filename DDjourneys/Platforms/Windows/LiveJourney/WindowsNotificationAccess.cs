using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Tracking.Live;
using Microsoft.Windows.AppNotifications;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>Windows has no permission prompt: only reports whether the user, policy or manifest blocks notifications.</summary>
internal sealed class WindowsNotificationAccess : INotificationAccess
{
	public Task<bool> CanNotifyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			return Task.FromResult(
				AppNotificationManager.Default.Setting is not (
					AppNotificationSetting.DisabledForApplication
					or AppNotificationSetting.DisabledForUser
					or AppNotificationSetting.DisabledByGroupPolicy
					or AppNotificationSetting.DisabledByManifest));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[NOTIFY] Setting unavailable: {ex.Message}");

			return Task.FromResult(true);
		}
	}

	public Task RequestAsync() => Task.CompletedTask;
}