using System.Collections.Concurrent;

namespace DDjourneys.Core.Services;

/// <summary>
/// A small time-limited cache for answers that hardly change while somebody looks at them (the lines of a stop, its
/// accessibility data): the same page asked again within the time costs no request. Only answers are stored, never failures.
/// </summary>
internal sealed class TtlCache<T>(TimeSpan lifetime, int capacity = 128)
{
	private readonly ConcurrentDictionary<string, (DateTimeOffset At, T Value)> _items = new(StringComparer.Ordinal);

	public bool TryGet(string key, out T value)
	{
		if (_items.TryGetValue(key, out (DateTimeOffset At, T Value) item)
			&& DateTimeOffset.UtcNow - item.At < lifetime)
		{
			value = item.Value;

			return true;
		}

		value = default!;

		return false;
	}

	public void Set(string key, T value)
	{
		if (_items.Count >= capacity)
		{
			_items.Clear();
		}

		_items[key] = (DateTimeOffset.UtcNow, value);
	}

	public void Clear() => _items.Clear();
}
