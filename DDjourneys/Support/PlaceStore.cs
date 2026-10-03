using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support;

/// <summary>
/// One searched connection: where the passenger wanted to go, and from where.
/// </summary>
public sealed record RoutePair(
	Location From,
	Location To);


/// <summary>
/// Remembers recent and favourite places, and the connections that were searched,
/// between app runs.
///
/// Small lists only: stored as JSON in MAUI Preferences. If this ever grows,
/// this class is the only thing to replace.
/// </summary>
public sealed class PlaceStore
{
	private const string RecentsKey = "places.recents";
	private const string FavouritesKey = "places.favourites";
	private const string RoutesKey = "places.routes";
	private const int MaxRecents = 8;

	/// <summary>Provider that stored stops without a provider id belong to (the app only knew VVO then).</summary>
	private const string LegacyProviderId = VvoProviderInfo.Id;

	/// <summary>How many searched connections are kept. Older ones drop off the end.</summary>
	public const int MaxRoutes = 50;

	private readonly object _gate = new();
	private readonly List<Location> _recents;
	private readonly List<Location> _favourites;
	private readonly List<RoutePair> _routes;
	private readonly WeakEventManager _weakEventManager = new();

	/// <summary>
	/// Raised after any change to recents or favourites.
	/// </summary>
	public event EventHandler? Changed
	{
		add =>
			_weakEventManager.AddEventHandler(
				value,
				nameof(Changed));

		remove =>
			_weakEventManager.RemoveEventHandler(
				value,
				nameof(Changed));
	}


	public PlaceStore()
	{
		_recents =
			Load(RecentsKey);

		_favourites =
			Load(FavouritesKey);

		_routes =
			LoadRoutes();
	}


	/// <summary>
	/// Most recently used first.
	/// </summary>
	public IReadOnlyList<Location> Recents
	{
		get
		{
			lock (_gate)
			{
				return _recents.ToArray();
			}
		}
	}


	/// <summary>
	/// In the order they were added.
	/// </summary>
	public IReadOnlyList<Location> Favourites
	{
		get
		{
			lock (_gate)
			{
				return _favourites.ToArray();
			}
		}
	}


	/// <summary>Most recently searched first.</summary>
	public IReadOnlyList<RoutePair> RecentRoutes
	{
		get
		{
			lock (_gate)
			{
				return _routes.ToArray();
			}
		}
	}


	/// <summary>
	/// Remembers a searched connection. Searching the same connection again moves it back to the
	/// top instead of adding a second entry.
	/// </summary>
	public void AddRecentRoute(
		Location from,
		Location to)
	{
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);

		lock (_gate)
		{
			string key =
				RouteKeyOf(from, to);

			_routes.RemoveAll(
				route => RouteKeyOf(route.From, route.To) == key);

			_routes.Insert(
				0,
				new RoutePair(from, to));

			if (_routes.Count > MaxRoutes)
			{
				_routes.RemoveRange(
					MaxRoutes,
					_routes.Count - MaxRoutes);
			}

			SaveRoutes();
		}

		RaiseChanged();
	}


	public void RemoveRecentRoute(
		RoutePair route)
	{
		ArgumentNullException.ThrowIfNull(route);

		lock (_gate)
		{
			string key =
				RouteKeyOf(route.From, route.To);

			if (_routes.RemoveAll(
					item => RouteKeyOf(item.From, item.To) == key) == 0)
			{
				return;
			}

			SaveRoutes();
		}

		RaiseChanged();
	}


	public void ClearRecentRoutes()
	{
		lock (_gate)
		{
			if (_routes.Count == 0)
			{
				return;
			}

			_routes.Clear();
			SaveRoutes();
		}

		RaiseChanged();
	}


	public void AddRecent(
		Location place)
	{
		ArgumentNullException.ThrowIfNull(
			place);

		lock (_gate)
		{
			string key =
				KeyOf(place);

			_recents.RemoveAll(
				p => KeyOf(p) == key);

			_recents.Insert(
				0,
				place);

			if (_recents.Count > MaxRecents)
			{
				_recents.RemoveRange(
					MaxRecents,
					_recents.Count - MaxRecents);
			}

			Save(
				RecentsKey,
				_recents);
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

			Save(
				RecentsKey,
				_recents);
		}

		RaiseChanged();
	}


	public bool IsFavourite(
		Location place)
	{
		ArgumentNullException.ThrowIfNull(
			place);

		lock (_gate)
		{
			string key =
				KeyOf(place);

			return _favourites.Any(
				p => KeyOf(p) == key);
		}
	}


	public void SetFavourite(
		Location place,
		bool isFavourite)
	{
		ArgumentNullException.ThrowIfNull(
			place);

		lock (_gate)
		{
			string key =
				KeyOf(place);

			bool exists =
				_favourites.Any(
					p => KeyOf(p) == key);

			if (exists == isFavourite)
			{
				return;
			}

			if (isFavourite)
			{
				_favourites.Add(
					place);
			}
			else
			{
				_favourites.RemoveAll(
					p => KeyOf(p) == key);
			}

			Save(
				FavouritesKey,
				_favourites);
		}

		RaiseChanged();
	}


	private void RaiseChanged()
	{
		try
		{
			_weakEventManager.HandleEvent(
				this,
				EventArgs.Empty,
				nameof(Changed));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"PlaceStore subscriber failed: {ex.Message}");
		}
	}


	// Stations are identified by provider and provider ID (the same raw ID can mean different stops at
	// different providers); free-form places by name and position.
	private static string RouteKeyOf(
		Location from,
		Location to) =>
		$"{KeyOf(from)}>{KeyOf(to)}";


	private static string KeyOf(
		Location place) =>
		place.StopKey is { } stopKey
			? $"id:{stopKey}"
			: FormattableString.Invariant(
				$"{place.Name}|{place.Place}|{place.Latitude}|{place.Longitude}");


	// Stored shape is independent of the domain model,
	// so the model can evolve without invalidating stored data.
	// ProviderId is optional: entries written before it existed have none (see ToLocation).
	private sealed record Entry(
		string? Id,
		string Name,
		string? Place,
		double? Latitude,
		double? Longitude,
		string? ProviderId = null);


	private static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
		new()
		{
			WriteIndented = false
		};


	/// <summary>Stored shape of a connection: the two endpoints, nothing else.</summary>
	private sealed record RouteEntry(
		Entry? From,
		Entry? To);


	private List<RoutePair> LoadRoutes()
	{
		try
		{
			string json =
				Preferences.Get(
					RoutesKey,
					string.Empty);

			if (string.IsNullOrWhiteSpace(json))
			{
				return [];
			}

			return
				(JsonSerializer.Deserialize<List<RouteEntry>>(json, JsonOptions)
					?? [])
				.Where(
					entry =>
						entry.From is not null
						&& entry.To is not null
						&& !string.IsNullOrWhiteSpace(entry.From.Name)
						&& !string.IsNullOrWhiteSpace(entry.To.Name))
				.Select(
					entry =>
						new RoutePair(
							ToLocation(entry.From!),
							ToLocation(entry.To!)))
				.Take(MaxRoutes)
				.ToList();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"PlaceStore load '{RoutesKey}' failed: {ex.Message}");

			return [];
		}
	}


	private void SaveRoutes()
	{
		try
		{
			var entries =
				_routes.Select(
					route =>
						new RouteEntry(
							ToEntry(route.From),
							ToEntry(route.To)));

			Preferences.Set(
				RoutesKey,
				JsonSerializer.Serialize(entries, JsonOptions));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"PlaceStore save '{RoutesKey}' failed: {ex.Message}");
		}
	}


	private static Entry ToEntry(
		Location place) =>
		new(
			place.Id,
			place.Name,
			place.Place,
			place.Latitude,
			place.Longitude,
			string.IsNullOrWhiteSpace(place.ProviderId)
				? null
				: place.ProviderId);


	/// <summary>
	/// Before provider ids existed the app only spoke to VVO, so a stored stop without a provider
	/// belongs to VVO. Free-form places have no provider.
	/// </summary>
	private static Location ToLocation(
		Entry entry) =>
		new()
		{
			Id = entry.Id,
			ProviderId =
				!string.IsNullOrWhiteSpace(entry.ProviderId)
					? entry.ProviderId
					: string.IsNullOrWhiteSpace(entry.Id)
						? string.Empty
						: LegacyProviderId,
			Name = entry.Name,
			Place = entry.Place,
			Latitude = entry.Latitude,
			Longitude = entry.Longitude
		};


	private static List<Location> Load(
		string key)
	{
		try
		{
			string json =
				Preferences.Get(
					key,
					string.Empty);

			if (string.IsNullOrWhiteSpace(json))
			{
				return [];
			}

			return
				(JsonSerializer.Deserialize<List<Entry>>(json)
					?? [])
				.Where(
					e =>
						!string.IsNullOrWhiteSpace(
							e.Name))
				.Select(ToLocation)
				.ToList();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"PlaceStore load '{key}' failed: {ex.Message}");

			return [];
		}
	}


	private static void Save(
		string key,
		List<Location> places)
	{
		try
		{
			var entries =
				places.Select(ToEntry);

			Preferences.Set(
				key,
				JsonSerializer.Serialize(entries));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"PlaceStore save '{key}' failed: {ex.Message}");
		}
	}
}