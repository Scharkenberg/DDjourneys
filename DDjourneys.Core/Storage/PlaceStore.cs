using System.Text.Json;
using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Storage;

/// <summary>
/// Remembers recent and favourite places between app runs.
///
/// Small lists only: stored as JSON in MAUI Preferences. If this ever grows,
/// this class is the only thing to replace.
/// </summary>
public sealed class PlaceStore
{
	private const string RecentsKey = "places.recents";
	private const string FavouritesKey = "places.favourites";
	private const int MaxRecents = 8;

	private readonly object _gate = new();
	private readonly List<Location> _recents;
	private readonly List<Location> _favourites;

	/// <summary>
	/// Raised after any change to recents or favourites.
	/// </summary>
	public event EventHandler? Changed;

	public PlaceStore()
	{
		_recents = Load(RecentsKey);
		_favourites = Load(FavouritesKey);
	}

	/// <summary>
	/// Most recently used first.
	/// </summary>
	public IReadOnlyList<Location> Recents
	{
		get { lock (_gate) { return _recents.ToArray(); } }
	}

	/// <summary>
	/// In the order they were added.
	/// </summary>
	public IReadOnlyList<Location> Favourites
	{
		get { lock (_gate) { return _favourites.ToArray(); } }
	}

	public void AddRecent(Location place)
	{
		ArgumentNullException.ThrowIfNull(place);

		lock (_gate)
		{
			string key = KeyOf(place);

			_recents.RemoveAll(p => KeyOf(p) == key);
			_recents.Insert(0, place);

			if (_recents.Count > MaxRecents)
			{
				_recents.RemoveRange(MaxRecents, _recents.Count - MaxRecents);
			}

			Save(RecentsKey, _recents);
		}

		RaiseChanged();
	}

	public void ClearRecents()
	{
		lock (_gate)
		{
			if (_recents.Count == 0)
			{
				return;
			}

			_recents.Clear();
			Save(RecentsKey, _recents);
		}

		RaiseChanged();
	}

	public bool IsFavourite(Location place)
	{
		ArgumentNullException.ThrowIfNull(place);

		lock (_gate)
		{
			string key = KeyOf(place);
			return _favourites.Any(p => KeyOf(p) == key);
		}
	}

	public void SetFavourite(Location place, bool isFavourite)
	{
		ArgumentNullException.ThrowIfNull(place);

		lock (_gate)
		{
			string key = KeyOf(place);
			bool exists = _favourites.Any(p => KeyOf(p) == key);

			if (exists == isFavourite)
			{
				return;
			}

			if (isFavourite)
			{
				_favourites.Add(place);
			}
			else
			{
				_favourites.RemoveAll(p => KeyOf(p) == key);
			}

			Save(FavouritesKey, _favourites);
		}

		RaiseChanged();
	}

	private void RaiseChanged()
	{
		try
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"PlaceStore subscriber failed: {ex.Message}");
		}
	}

	// Stations are identified by provider ID, free-form places by name and position.
	private static string KeyOf(Location place) =>
		place.IsStation
			? $"id:{place.Id}"
			: FormattableString.Invariant(
				$"{place.Name}|{place.Place}|{place.Latitude}|{place.Longitude}");

	// Stored shape is independent of the domain model, so the model can evolve.
	private sealed record Entry(
		string? Id,
		string Name,
		string? Place,
		double? Latitude,
		double? Longitude);

	private static List<Location> Load(string key)
	{
		try
		{
			string json = Preferences.Get(key, string.Empty);

			if (string.IsNullOrWhiteSpace(json))
			{
				return [];
			}

			return (JsonSerializer.Deserialize<List<Entry>>(json) ?? [])
				.Where(e => !string.IsNullOrWhiteSpace(e.Name))
				.Select(e => new Location
				{
					Id = e.Id,
					Name = e.Name,
					Place = e.Place,
					Latitude = e.Latitude,
					Longitude = e.Longitude
				})
				.ToList();
		}
		catch (Exception ex)
		{
			// Unreadable or inaccessible data must never stop the app from starting.
			System.Diagnostics.Debug.WriteLine($"PlaceStore load '{key}' failed: {ex.Message}");
			return [];
		}
	}

	private static void Save(string key, List<Location> places)
	{
		try
		{
			var entries = places.Select(p => new Entry(
				p.Id, p.Name, p.Place, p.Latitude, p.Longitude));

			Preferences.Set(key, JsonSerializer.Serialize(entries));
		}
		catch (Exception ex)
		{
			// Losing persistence must not break the running session.
			System.Diagnostics.Debug.WriteLine($"PlaceStore save '{key}' failed: {ex.Message}");
		}
	}
}