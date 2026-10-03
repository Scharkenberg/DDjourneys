using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DDjourneys.Core.Tracking;

/// <summary>
/// Fan-out of events to any number of independent async enumerations.
/// A single shared channel would hand each event to only one of several readers.
/// </summary>
public sealed class TrackingEventBroadcaster<T> : IDisposable
{
	private readonly object _gate = new();
	private readonly List<Channel<T>> _subscribers = [];
	private bool _disposed;

	public void Publish(T item)
	{
		Channel<T>[] snapshot;

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

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

		bool closed;

		lock (_gate)
		{
			closed = _disposed;

			if (!closed)
			{
				_subscribers.Add(channel);
			}
		}

		if (closed)
		{
			// Nothing will ever be published again: end the enumeration at once.
			yield break;
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

	/// <summary>
	/// Completes every subscription (their enumerations end after the items already queued)
	/// and ignores later publishes. Idempotent.
	/// </summary>
	public void Dispose()
	{
		Channel<T>[] snapshot;

		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			snapshot = [.. _subscribers];
			_subscribers.Clear();
		}

		foreach (Channel<T> channel in snapshot)
		{
			channel.Writer.TryComplete();
		}
	}
}
