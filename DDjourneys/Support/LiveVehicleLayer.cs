using System.Collections.Concurrent;
using DDjourneys.Core.Mapping;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Support;

/// <summary>
/// The vehicles layer of the explore map: it rides the one shared stream (the hub) and publishes, on the
/// page's tick, only the "vehicles" layer - the per-layer bridge leaves every other layer alone, which is
/// the point of it. While the switch is on it stays subscribed even below the publishing zoom; the last
/// positions age out quietly when the stream dies (the Vehicles page stays the place for connection detail).
/// </summary>
public sealed class LiveVehicleLayer : IDisposable
{
	private const string IdPrefix = "v:";

	/// <summary>Below this zoom a full-vehicle map is noise; the subscription stays, the publish waits.</summary>
	public const double MinZoom = 13;

	private readonly VehicleStreamHub _hub;
	private readonly ConcurrentDictionary<string, LiveVehicle> _latest = new(StringComparer.Ordinal);
	private VehicleStreamSubscription? _subscription;
	private string? _lastJson;
	private volatile bool _dirty;
	private bool _disposed;

	public LiveVehicleLayer(VehicleStreamHub hub)
	{
		ArgumentNullException.ThrowIfNull(hub);

		_hub = hub;
	}

	/// <summary>Whether the layer is receiving positions right now.</summary>
	public bool IsRunning => _subscription is not null;

	/// <summary>Attach to the stream (again); nothing is cleared - the last positions may still be worth showing.</summary>
	public void Start()
	{
		if (_disposed
				|| _subscription is not null)
		{
			return;
		}

		_subscription = _hub.Subscribe();
		_ = PumpAsync(_subscription);
	}

	/// <summary>Detach from the stream; the positions that arrived stay until they age out.</summary>
	public void Pause()
	{
		_subscription?.Dispose();
		_subscription = null;
	}

	/// <summary>The switch was turned off: forget everything, including what was sent.</summary>
	public void Reset()
	{
		_latest.Clear();
		_lastJson = null;
		_dirty = false;
	}

	/// <summary>The theme changed: the next tick sends the same positions in the new colours.</summary>
	public void MarkDirty() => _dirty = true;

	/// <summary>The vehicle behind a marker id ("v:&lt;Key&gt;"), when it is still around.</summary>
	public bool TryGet(string markerId, out LiveVehicle vehicle)
	{
		if (markerId.StartsWith(IdPrefix, StringComparison.Ordinal)
				&& _latest.TryGetValue(markerId[IdPrefix.Length..], out LiveVehicle? found))
		{
			vehicle = found;

			return true;
		}

		vehicle = null!;

		return false;
	}

	/// <summary>
	/// The page's tick: the layer payload for the viewport, or null when there is nothing to send (nothing new,
	/// no viewport, below the publishing zoom). The caller sends it to the map as the "vehicles" layer.
	/// </summary>
	public MapScene? Publish(MapViewport? viewport, bool dark)
	{
		if (!_dirty
				|| viewport is null
				|| viewport.Zoom < MinZoom)
		{
			return null;
		}

		_dirty = false;

		DateTimeOffset now = DateTimeOffset.UtcNow;
		bool aged = false;

		foreach (KeyValuePair<string, LiveVehicle> entry in _latest)
		{
			if (now - entry.Value.Time > TimeSpan.FromSeconds(90)
					&& _latest.TryRemove(entry.Key, out _))
			{
				aged = true;
			}
		}

		IReadOnlyList<LiveVehicle> inView = VehicleLayering.Within(viewport, _latest.Values, now: now);

		if (inView.Count == 0)
		{
			// Nothing worth showing any more: clear the layer once, then stay quiet.
			if (_lastJson is null)
			{
				return null;
			}

			_lastJson = null;

			return MapScene.Empty;
		}

		MapScene scene = MapScenes.FromVehicles(inView, fit: false, idPrefix: IdPrefix);

		string json = scene.ToJson(dark, MapScenes.Resolve);

		if (json == _lastJson)
		{
			return null;
		}

		_lastJson = json;

		return scene;
	}

	public void Dispose()
	{
		_disposed = true;
		_subscription?.Dispose();
		_subscription = null;
	}

	private async Task PumpAsync(VehicleStreamSubscription subscription)
	{
		await foreach (LiveVehicle vehicle in subscription.Reader.ReadAllAsync())
		{
			if (_disposed)
			{
				return;
			}

			_latest[vehicle.Key] = vehicle;
			_dirty = true;
		}
	}
}
