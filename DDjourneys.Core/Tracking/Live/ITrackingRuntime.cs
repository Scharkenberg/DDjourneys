namespace DDjourneys.Core.Tracking.Live;

/// <summary>
/// Keeps the tracker's polling running while a journey is under way. On Android this is a
/// foreground service (the process would otherwise be frozen with the screen off); platforms
/// without such a mechanism poll while the app runs (<see cref="InProcessTrackingRuntime"/>).
/// </summary>
public interface ITrackingRuntime
{
	/// <summary>Asks the platform to keep the app alive; false if it refused (try again later).</summary>
	bool TryKeepAlive();

	/// <summary>Monitoring is no longer needed.</summary>
	void Release();
}

/// <summary>Polling lives as long as the app; nothing to start or stop.</summary>
public sealed class InProcessTrackingRuntime : ITrackingRuntime
{
	public bool TryKeepAlive() => true;

	public void Release()
	{
	}
}

/// <summary>Whether the user lets the app post the notifications that carry live updates.</summary>
public interface INotificationAccess
{
	/// <summary>False only when notifications are blocked; true where nothing can block them.</summary>
	Task<bool> CanNotifyAsync(CancellationToken cancellationToken = default);

	/// <summary>Asks for permission where the platform requires it. Never throws.</summary>
	Task RequestAsync();
}

/// <summary>Platforms without a notification permission.</summary>
public sealed class UnrestrictedNotificationAccess : INotificationAccess
{
	public Task<bool> CanNotifyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

	public Task RequestAsync() => Task.CompletedTask;
}
