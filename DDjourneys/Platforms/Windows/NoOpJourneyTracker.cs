using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Windows;

/// <summary>Tracker for platforms without a monitoring implementation: nothing is followed.</summary>
public sealed class NoOpJourneyTracker : IJourneyTracker
{
	public bool IsAvailable => false;

	public IAsyncEnumerable<JourneyTrackingEvent> Events => Empty();

	public IReadOnlyList<WatchedJourney> Watched => [];

	public event EventHandler? WatchedChanged
	{
		add { }
		remove { }
	}

	public Task<bool> CanNotifyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

	public WatchedJourney? Find(Journey journey) => null;

	public Task<WatchedJourney> FollowAsync(Journey journey, string? replacesPlanId = null, CancellationToken cancellationToken = default) =>
		Task.FromException<WatchedJourney>(
			new NotSupportedException("Journey tracking is not available on this platform."));

	public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task SetActiveAsync(string planId, bool active, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	public Task SetOptionsAsync(string planId, WatchOptions options, CancellationToken cancellationToken = default) =>
		Task.CompletedTask;

	public Task DeleteAsync(string planId, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task DeleteAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

	private static async IAsyncEnumerable<JourneyTrackingEvent> Empty()
	{
		await Task.CompletedTask;
		yield break;
	}
}
