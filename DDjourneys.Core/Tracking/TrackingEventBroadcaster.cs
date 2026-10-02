using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DDjourneys.Core.Tracking;

/// <summary>
/// Fan-out of events to any number of independent async enumerations.
/// A single shared channel would hand each event to only one of several readers.
/// </summary>
public sealed class TrackingEventBroadcaster<T>
{
	private readonly object _gate = new();
	private readonly List<Channel<T>> _subscribers = [];

	public void Publish(T item)
	{
		Channel<T>[] snapshot;

		lock (_gate)
		{
			snapshot = [.. _subscribers];
		}

		foreach (Channel<T> channel in snapshot)
		{
			channel.Writer.TryWrite(item);
		}
	}

	/// <summary>The subscription is registered when enumeration starts and ends with it.</summary>
	public async IAsyncEnumerable<T> SubscribeAsync(
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });

		lock (_gate)
		{
			_subscribers.Add(channel);
		}

		try
		{
			await foreach (T item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				yield return item;
			}
		}
		finally
		{
			lock (_gate)
			{
				_subscribers.Remove(channel);
			}

			channel.Writer.TryComplete();
		}
	}
}
