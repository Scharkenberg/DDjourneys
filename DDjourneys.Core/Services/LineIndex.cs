using System.Text.RegularExpressions;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>One line the index knows, and where the app last saw it.</summary>
public sealed record IndexedLine(
	string ProviderId,
	string Name,
	TransitMode Mode,
	Station SeenAt,
	DateTimeOffset LastSeenUtc);

/// <summary>
/// The lines the app has seen while it loaded boards and stop lines, per provider — the candidates of
/// the place search's line rows ("11" names a line as well as a place). It is built as a side effect of
/// normal loads, in memory only: nothing is ever asked of a provider for it, and after a restart it fills
/// again as stops are opened. A line is one entry no matter how many stops showed it (the stop last seen
/// is kept); entries age out when they have not been seen for the lifetime, and the oldest go first when
/// a provider's share exceeds the capacity.
/// </summary>
public sealed partial class LineIndex
{
	/// <summary>How long an unseen line stays a search candidate.</summary>
	public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

	/// <summary>How many lines one provider may collect.</summary>
	public const int DefaultCapacity = 400;

	private readonly Lock _gate = new();
	private readonly TimeSpan _lifetime;
	private readonly int _capacity;
	private readonly Dictionary<string, Dictionary<string, IndexedLine>> _lines = new(StringComparer.OrdinalIgnoreCase);

	public LineIndex(TimeSpan? lifetime = null, int capacity = DefaultCapacity)
	{
		_lifetime = lifetime ?? DefaultLifetime;
		_capacity = Math.Max(1, capacity);
	}

	/// <summary>
	/// Remembers a line of a provider, and the stop it was seen at. The same line (same key, see
	/// <see cref="BoardFilter.LineKey"/>) is one entry; seeing it again only refreshes where and when.
	/// </summary>
	public void Remember(string providerId, TransitLine line, Station seenAt)
	{
		ArgumentNullException.ThrowIfNull(line);
		ArgumentNullException.ThrowIfNull(seenAt);

		if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(line.Name))
		{
			return;
		}

		string key = BoardFilter.LineKey(line.Name);

		lock (_gate)
		{
			if (!_lines.TryGetValue(providerId, out Dictionary<string, IndexedLine>? bucket))
			{
				bucket = new Dictionary<string, IndexedLine>(StringComparer.OrdinalIgnoreCase);
				_lines[providerId] = bucket;
			}

			DateTimeOffset now = DateTimeOffset.UtcNow;

			// The name first seen stays the shown one; a mode that was unknown may still become known.
			if (bucket.TryGetValue(key, out IndexedLine? seen))
			{
				bucket[key] = seen with
				{
					Mode = line.Mode != TransitMode.Unknown ? line.Mode : seen.Mode,
					SeenAt = seenAt,
					LastSeenUtc = now
				};

				return;
			}

			bucket[key] = new IndexedLine(providerId, line.Name.Trim(), line.Mode, seenAt, now);
		}
	}

	/// <summary>
	/// The provider's lines whose name matches the query, best first (the exact name, then the shortest
	/// that begin with it), at most <paramref name="limit"/>.
	/// </summary>
	public IReadOnlyList<IndexedLine> Search(string providerId, string query, int limit = 4)
	{
		if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(query))
		{
			return [];
		}

		string key = BoardFilter.LineKey(query);

		Prune(DateTimeOffset.UtcNow);

		lock (_gate)
		{
			if (!_lines.TryGetValue(providerId, out Dictionary<string, IndexedLine>? bucket))
			{
				return [];
			}

			return
				[.. bucket.Values
					.Select(line => (Line: line, Key: BoardFilter.LineKey(line.Name)))
					.Where(entry => entry.Key == key || entry.Key.StartsWith(key, StringComparison.Ordinal))
					.OrderBy(entry => entry.Key == key ? 0 : 1)
					.ThenBy(entry => entry.Key.Length)
					.ThenBy(entry => entry.Key, StringComparer.Ordinal)
					.Take(Math.Max(0, limit))
					.Select(entry => entry.Line)];
		}
	}

	/// <summary>Drops entries past their lifetime, and the oldest ones beyond the capacity.</summary>
	internal void Prune(DateTimeOffset nowUtc)
	{
		lock (_gate)
		{
			foreach (Dictionary<string, IndexedLine> bucket in _lines.Values)
			{
				if (bucket.Count == 0)
				{
					continue;
				}

				foreach (string aged in bucket
					.Where(entry => nowUtc - entry.Value.LastSeenUtc >= _lifetime)
					.Select(entry => entry.Key)
					.ToArray())
				{
					bucket.Remove(aged);
				}

				if (bucket.Count > _capacity)
				{
					foreach (string oldest in bucket
						.OrderBy(entry => entry.Value.LastSeenUtc)
						.Take(bucket.Count - _capacity)
						.Select(entry => entry.Key)
						.ToArray())
					{
						bucket.Remove(oldest);
					}
				}
			}
		}
	}

	/// <summary>The stop as the index keeps it (where a line was seen).</summary>
	internal static Station StopOf(Location stop) =>
		new()
		{
			Id = stop.Id ?? string.Empty,
			ProviderId = stop.ProviderId,
			Name = stop.Name,
			Place = stop.Place,
			Latitude = stop.Latitude,
			Longitude = stop.Longitude
		};

	/// <summary>
	/// A query that names a line rather than a place: digits with an optional short letter prefix and an
	/// optional trailing letter — "11", "85", "S1", "S 1", "RE01". Spaces do not count; anything with more
	/// or other characters is a place.
	/// </summary>
	public static bool IsLineQuery(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string compact = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

		return compact.Length is >= 1 and <= 6
			&& LinePattern().IsMatch(compact);
	}

	/// <summary>The shape of a line label: up to three letters, one to four digits, an optional trailing letter.</summary>
	[GeneratedRegex(@"^[A-Za-z]{0,3}[0-9]{1,4}[A-Za-z]?$")]
	private static partial Regex LinePattern();
}
