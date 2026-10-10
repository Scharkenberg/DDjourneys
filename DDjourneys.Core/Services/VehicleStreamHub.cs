using System.Threading.Channels;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>Where the one shared stream stands.</summary>
public enum VehicleStreamState
{
	/// <summary>Nobody is listening; the socket is closed.</summary>
	Idle,

	/// <summary>A session is opening or re-opening.</summary>
	Connecting,

	/// <summary>Positions are arriving.</summary>
	Live,

	/// <summary>The stream ended or failed; the hub reconnects.</summary>
	Degraded
}

/// <summary>
/// The handle a consumer holds: what arrives from the stream, until it is disposed. Disposing detaches the
/// consumer; when the last one leaves, the socket lingers a moment before it is closed.
/// </summary>
public sealed class VehicleStreamSubscription(ChannelReader<LiveVehicle> reader, Action disposed) : IDisposable
{
	public ChannelReader<LiveVehicle> Reader { get; } = reader;

	public void Dispose() => disposed();
}

/// <summary>
/// One TLMS stream for every consumer (the Vehicles page, the map's vehicle layer): each consumer that
/// opens its own session gets its own WebSocket to the same city, so the hub owns the single unfiltered
/// session and hands every subscriber a bounded channel - a slow consumer drops the oldest positions
/// instead of holding the loop back. It starts with the first subscriber and stops, after a short linger
/// (pane switches do not churn the socket), when the last one leaves. The reconnect ladder, once in the
/// Vehicles page, lives here now.
/// </summary>
public sealed class VehicleStreamHub : IDisposable
{
	private const int ChannelCapacity = 512;

	/// <summary>How long the socket stays up after the last consumer left, so pane switches do not churn it.</summary>
	public static readonly TimeSpan DefaultLinger = TimeSpan.FromSeconds(10);

	/// <summary>The pauses between the reconnects of a broken stream; the last one repeats.</summary>
	private static readonly TimeSpan[] ReconnectPauses =
		[TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)];

	private readonly ILiveVehicleProvider? _provider;
	private readonly TimeSpan _linger;
	private readonly Lock _gate = new();
	private readonly List<Channel<LiveVehicle>> _channels = [];
	private CancellationTokenSource? _session;
	private Timer? _lingerTimer;
	private VehicleStreamState _state = VehicleStreamState.Idle;
	private bool _running;

	/// <summary>Where the stream stands; fired on a background thread - consumers marshal before touching views.</summary>
	public event Action<VehicleStreamState>? StateChanged;

	public VehicleStreamHub(
		IEnumerable<ILiveVehicleProvider> providers,
		TimeSpan? linger = null)
	{
		_provider = providers.FirstOrDefault();
		_linger = linger ?? DefaultLinger;
	}

	public bool IsAvailable => _provider is not null;

	public VehicleStreamState State
	{
		get
		{
			lock (_gate)
			{
				return _state;
			}
		}
	}

	public VehicleStreamSubscription Subscribe()
	{
		Channel<LiveVehicle> channel =
			Channel.CreateBounded<LiveVehicle>(
				new BoundedChannelOptions(ChannelCapacity)
				{
					FullMode = BoundedChannelFullMode.DropOldest
				});

		CancellationTokenSource? start = null;

		lock (_gate)
		{
			_channels.Add(channel);

			if (_lingerTimer is not null)
			{
				_lingerTimer.Dispose();
				_lingerTimer = null;
			}

			// A session that was asked to end (the linger ran out) is as good as gone: a consumer arriving in
			// the moment before it winds down starts a fresh one instead of being completed with it.
			if ((!_running || _session is { IsCancellationRequested: true })
				&& _provider is not null)
			{
				_running = true;
				start = new CancellationTokenSource();
				_session = start;
			}
		}

		if (start is not null)
		{
			_ = RunAsync(start);
		}

		return new VehicleStreamSubscription(channel.Reader, () => Unsubscribe(channel));
	}

	private void Unsubscribe(Channel<LiveVehicle> channel)
	{
		Timer? arm = null;

		// A detached reader must end: a consumer that pumps the channel would wait on it for ever.
		channel.Writer.TryComplete();

		lock (_gate)
		{
			_channels.Remove(channel);

			if (_channels.Count == 0
					&& _running
					&& _lingerTimer is null)
			{
				arm = new Timer(
					_ => CloseAfterLinger(),
					null,
					_linger,
					Timeout.InfiniteTimeSpan);
				_lingerTimer = arm;
			}
		}
	}

	private void CloseAfterLinger()
	{
		lock (_gate)
		{
			if (_lingerTimer is not null)
			{
				_lingerTimer.Dispose();
				_lingerTimer = null;
			}

			if (_channels.Count > 0)
			{
				return;
			}

			_session?.Cancel();
		}
	}

	private async Task RunAsync(CancellationTokenSource session)
	{
		int failures = 0;
		CancellationToken token = session.Token;

		while (!token.IsCancellationRequested)
		{
			SetState(VehicleStreamState.Connecting);

			bool delivered = false;

			try
			{
				await foreach (LiveVehicle vehicle in
					_provider!.StreamAsync(
						new VehicleFilter(),
						token))
				{
					if (token.IsCancellationRequested)
					{
						break;
					}

					delivered = true;
					SetState(VehicleStreamState.Live);

					FanOut(vehicle);
				}
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write($"Live vehicles failed: {ex}");
			}

			if (token.IsCancellationRequested)
			{
				break;
			}

			// The stream ended without anybody asking: reconnect after a pause. A stream that carried
			// positions gets another go at once; a stream nobody watches keeps trying with a pause.
			SetState(VehicleStreamState.Degraded);

			failures = delivered ? 0 : failures + 1;

			try
			{
				await Task.Delay(ReconnectPauses[Math.Min(failures, ReconnectPauses.Length - 1)], token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}

		// Only the session that is still the current one winds the hub down; an older one that a newer
		// has replaced leaves the channels (and the state) to its successor.
		if (CompleteAll(session))
		{
			SetState(VehicleStreamState.Idle);
		}
	}

	private void FanOut(LiveVehicle vehicle)
	{
		lock (_gate)
		{
			foreach (Channel<LiveVehicle> channel in _channels)
			{
				channel.Writer.TryWrite(vehicle);
			}
		}
	}

	private bool CompleteAll(CancellationTokenSource session)
	{
		List<Channel<LiveVehicle>> rest;

		lock (_gate)
		{
			if (!ReferenceEquals(_session, session))
			{
				return false;
			}

			rest = [.. _channels];
			_channels.Clear();
			_running = false;
			_session = null;
		}

		foreach (Channel<LiveVehicle> channel in rest)
		{
			channel.Writer.TryComplete();
		}

		return true;
	}

	private void SetState(VehicleStreamState next)
	{
		Action<VehicleStreamState>? fire = null;

		lock (_gate)
		{
			if (_state == next)
			{
				return;
			}

			_state = next;
			fire = StateChanged;
		}

		fire?.Invoke(next);
	}

	public void Dispose()
	{
		Timer? timer = null;

		lock (_gate)
		{
			timer = _lingerTimer;
			_lingerTimer = null;
			_session?.Cancel();
		}

		timer?.Dispose();
	}
}
