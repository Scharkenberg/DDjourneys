using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Windows;

public sealed class NoOpJourneyTracker : IJourneyTracker
{
	public bool IsAvailable => false;
	public IAsyncEnumerable<JourneyTrackingEvent> Events => Empty();
	public Task StartAsync(Journey journey, CancellationToken cancellationToken = default) => Task.CompletedTask;
	public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	private static async IAsyncEnumerable<JourneyTrackingEvent> Empty()
	{
		await Task.CompletedTask;
		yield break;
	}
}
