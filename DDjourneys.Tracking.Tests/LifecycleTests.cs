using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;

namespace DDjourneys.Tracking.Tests;

/// <summary>Disposal of the broadcaster and the injected platform callback bridge.</summary>
public sealed class LifecycleTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

	// ----- Broadcaster -----

	[Fact]
	public async Task Disposing_the_broadcaster_ends_every_subscription_after_the_queued_items()
	{
		var broadcaster = new TrackingEventBroadcaster<int>();
		using var cancellation = new CancellationTokenSource(Limit);

		var received = new List<int>();
		var started = new TaskCompletionSource();

		Task reader =
			Task.Run(
				async () =>
				{
					await foreach (int item in broadcaster.SubscribeAsync(cancellation.Token))
					{
						received.Add(item);
						started.TrySetResult();
					}
				});

		await Task.Delay(100, cancellation.Token);

		broadcaster.Publish(1);
		await started.Task.WaitAsync(Limit);

		broadcaster.Dispose();

		await reader.WaitAsync(Limit);

		Assert.Equal([1], received);
	}

	[Fact]
	public async Task A_disposed_broadcaster_ignores_publishes_and_ends_new_subscriptions()
	{
		var broadcaster = new TrackingEventBroadcaster<int>();

		broadcaster.Dispose();
		broadcaster.Dispose();
		broadcaster.Publish(1);

		using var cancellation = new CancellationTokenSource(Limit);

		var received = new List<int>();

		await foreach (int item in broadcaster.SubscribeAsync(cancellation.Token))
		{
			received.Add(item);
		}

		Assert.Empty(received);
	}

	// ----- Callback bridge -----

	[Fact]
	public async Task The_bridge_does_nothing_without_a_tracker()
	{
		var bridge = new TrackingCallbackBridge();

		bridge.ServiceStopped();
		await bridge.HandleActionAsync("pause", "plan");
		await bridge.ResumeAsync();

		Assert.False(bridge.IsAttached);
	}

	[Fact]
	public async Task The_bridge_forwards_to_the_attached_tracker()
	{
		var bridge = new TrackingCallbackBridge();
		var tracker = new Recorder();

		bridge.Attach(tracker);

		bridge.ServiceStopped();
		await bridge.HandleActionAsync("pause", "plan-1");
		await bridge.ResumeAsync();

		Assert.True(bridge.IsAttached);
		Assert.Equal(1, tracker.Stopped);
		Assert.Equal(["pause:plan-1"], tracker.Actions);
		Assert.Equal(1, tracker.Resumed);
	}

	[Fact]
	public void Only_the_attached_tracker_can_detach_itself()
	{
		var bridge = new TrackingCallbackBridge();
		var first = new Recorder();
		var second = new Recorder();

		bridge.Attach(first);
		bridge.Attach(second);

		// The first tracker was replaced; its late disposal must not unplug the new one.
		bridge.Detach(first);
		Assert.True(bridge.IsAttached);

		bridge.ServiceStopped();
		Assert.Equal(0, first.Stopped);
		Assert.Equal(1, second.Stopped);

		bridge.Detach(second);
		Assert.False(bridge.IsAttached);
	}

	private sealed class Recorder : ITrackingCallbacks
	{
		public int Stopped { get; private set; }

		public int Resumed { get; private set; }

		public List<string> Actions { get; } = [];

		public void ServiceStopped() => Stopped++;

		public Task HandleActionAsync(string action, string planId)
		{
			Actions.Add($"{action}:{planId}");

			return Task.CompletedTask;
		}

		public Task ResumeAsync(CancellationToken cancellationToken = default)
		{
			Resumed++;

			return Task.CompletedTask;
		}
	}
}
