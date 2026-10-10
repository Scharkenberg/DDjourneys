using System.Threading.Channels;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The one shared stream of Tier 2: every consumer gets its own channel from the single session, the
/// reconnect ladder lives in the hub, and the last consumer leaving closes the socket after a linger.
/// A fake provider stands in for TLMS (the tracking tests fake providers the same way).
/// </summary>
public class VehicleStreamHubTests
{
	private static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(150);

	private static LiveVehicle At(int line, int run = 1, double latitude = 51.05, double longitude = 13.75) =>
		new()
		{
			Line = line,
			Run = run,
			Latitude = latitude,
			Longitude = longitude,
			Time = DateTimeOffset.UtcNow
		};

	[Fact]
	public async Task Two_subscribers_both_receive_what_arrives()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription first = hub.Subscribe();
		VehicleStreamSubscription second = hub.Subscribe();

		await provider.PushAsync(At(8));
		await provider.PushAsync(At(8, run: 2));

		List<LiveVehicle> firstSeen = await DrainAsync(first.Reader, 2);
		List<LiveVehicle> secondSeen = await DrainAsync(second.Reader, 2);

		Assert.Equal([8, 8], firstSeen.Select(vehicle => vehicle.Line).ToArray());
		Assert.Equal([8, 8], secondSeen.Select(vehicle => vehicle.Line).ToArray());

		first.Dispose();
		second.Dispose();
		hub.Dispose();
	}

	[Fact]
	public async Task The_shared_session_is_unfiltered()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription subscriber = hub.Subscribe();
		await provider.PushAsync(At(8));

		await DrainAsync(subscriber.Reader, 1);

		Assert.Single(provider.Filters);
		Assert.Empty(provider.Filters[0].Lines);

		subscriber.Dispose();
		hub.Dispose();
	}

	[Fact]
	public async Task A_burst_drops_the_oldest_for_a_subscriber_who_reads_slowly()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription subscriber = hub.Subscribe();

		for (int index = 0; index < 600; index++)
		{
			provider.Incoming.Writer.TryWrite(At(8, run: index));
		}

		await provider.EndSessionAsync();

		// The end of the session is the proof that all 600 were fanned out; only then is the
		// channel's content final. Until then, reading could race the writer and see the early positions.
		await WaitUntilAsync(() => hub.State == VehicleStreamState.Degraded, TimeSpan.FromSeconds(5));

		List<LiveVehicle> seen = await DrainAsync(subscriber.Reader, 512);

		// The channel holds 512; the first 88 positions of the burst were dropped, not the newest.
		Assert.Equal(512, seen.Count);
		Assert.Equal(88, seen[0].Run);
		Assert.Equal(599, seen[^1].Run);

		subscriber.Dispose();
		hub.Dispose();
	}

	[Fact]
	public async Task The_last_subscriber_leaving_stops_the_session_after_the_linger()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription subscriber = hub.Subscribe();
		await provider.PushAsync(At(8));
		await DrainAsync(subscriber.Reader, 1);

		Assert.Equal(VehicleStreamState.Live, hub.State);

		subscriber.Dispose();

		await WaitUntilAsync(
			() => hub.State == VehicleStreamState.Idle && provider.LastToken.IsCancellationRequested,
			TimeSpan.FromSeconds(5));

		Assert.Equal(VehicleStreamState.Idle, hub.State);
		Assert.True(provider.LastToken.IsCancellationRequested);

		hub.Dispose();
	}

	[Fact]
	public async Task A_new_subscriber_during_the_linger_keeps_the_session()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription first = hub.Subscribe();
		first.Dispose();

		await Task.Delay(30);

		// Within the linger: the socket should still be up, so this subscriber joins the same session.
		VehicleStreamSubscription second = hub.Subscribe();
		await Task.Delay(Linger + TimeSpan.FromSeconds(1));

		// The linger window has passed and the session must still be standing: a position arrives.
		Assert.False(provider.LastToken.IsCancellationRequested);

		await provider.PushAsync(At(8, run: 42));

		List<LiveVehicle> seen = await DrainAsync(second.Reader, 1);

		Assert.Equal(42, seen[0].Run);

		second.Dispose();
		hub.Dispose();
	}

	[Fact]
	public async Task A_failing_session_is_reported_as_degraded()
	{
		FakeLiveProvider provider = new() { ThrowNext = new InvalidOperationException("tlms down") };
		VehicleStreamHub hub = new([provider], Linger);

		var states = new System.Collections.Concurrent.ConcurrentQueue<VehicleStreamState>();
		hub.StateChanged += states.Enqueue;

		VehicleStreamSubscription subscriber = hub.Subscribe();

		await WaitUntilAsync(() => states.ToArray().Contains(VehicleStreamState.Degraded), TimeSpan.FromSeconds(5));

		Assert.Contains(VehicleStreamState.Degraded, states.ToArray());

		subscriber.Dispose();
		hub.Dispose();
	}

	[Fact]
	public async Task A_slow_pump_still_sees_the_end_of_the_stream()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription subscriber = hub.Subscribe();

		await provider.PushAsync(At(8));
		await provider.EndSessionAsync();

		// The hub reconnects (a delivered stream gets another go), but the channel is not completed while subscribed.
		List<LiveVehicle> seen = await DrainAsync(subscriber.Reader, 1);
		Assert.Single(seen);

		subscriber.Dispose();
		hub.Dispose();
	}

	[Fact]
	public async Task A_detached_reader_ends_instead_of_waiting_for_ever()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription subscriber = hub.Subscribe();

		await provider.PushAsync(At(8));
		await DrainAsync(subscriber.Reader, 1);

		subscriber.Dispose();

		// A pump over the reader (the map's layer) finishes when its subscription is disposed.
		Task completion = subscriber.Reader.Completion;

		Assert.Same(completion, await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(2))));

		hub.Dispose();
	}

	[Fact]
	public async Task A_subscriber_after_the_linger_started_gets_a_live_session()
	{
		FakeLiveProvider provider = new();
		VehicleStreamHub hub = new([provider], Linger);

		VehicleStreamSubscription first = hub.Subscribe();

		first.Dispose();

		// Let the linger run out and the session wind down, then come back.
		await WaitUntilAsync(() => hub.State == VehicleStreamState.Idle, TimeSpan.FromSeconds(3));

		VehicleStreamSubscription second = hub.Subscribe();

		await WaitUntilAsync(() => provider.Filters.Count >= 2, TimeSpan.FromSeconds(3));
		await provider.PushAsync(At(3));

		Assert.Equal(3, (await DrainAsync(second.Reader, 1))[0].Line);

		second.Dispose();
		hub.Dispose();
	}

	private static async Task<List<LiveVehicle>> DrainAsync(ChannelReader<LiveVehicle> reader, int expected)
	{
		List<LiveVehicle> seen = [];

		while (seen.Count < expected)
		{
			seen.Add(await reader.ReadAsync());
		}

		return seen;
	}

	private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan limit)
	{
		DateTimeOffset until = DateTimeOffset.UtcNow + limit;

		while (DateTimeOffset.UtcNow < until)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(20);
		}

		throw new TimeoutException("the hub did not reach the expected state");
	}

	/// <summary>Stands in for TLMS: the test pushes into a channel, the hub reads it as a session.</summary>
	private sealed class FakeLiveProvider : ILiveVehicleProvider
	{
		public Channel<LiveVehicle> Incoming { get; } = Channel.CreateUnbounded<LiveVehicle>();

		public List<VehicleFilter> Filters { get; } = [];

		public CancellationToken LastToken { get; private set; }

		public Exception? ThrowNext { get; set; }

		public async IAsyncEnumerable<LiveVehicle> StreamAsync(
			VehicleFilter filter,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			Filters.Add(filter);
			LastToken = cancellationToken;

			if (ThrowNext is { } failure)
			{
				ThrowNext = null;

				throw failure;
			}

			await foreach (LiveVehicle vehicle in Incoming.Reader.ReadAllAsync(cancellationToken))
			{
				yield return vehicle;
			}
		}

		public Task PushAsync(LiveVehicle vehicle)
		{
			Incoming.Writer.TryWrite(vehicle);

			return Task.CompletedTask;
		}

		public Task EndSessionAsync()
		{
			Incoming.Writer.TryComplete();

			return Task.CompletedTask;
		}
	}
}
