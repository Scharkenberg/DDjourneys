using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;
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
/// Small lists only: stored as versioned JSON (<see cref="StoredJson"/>) in MAUI Preferences. Reading is
/// entry by entry, so one unreadable entry never costs the list; every write keeps the previous text as
/// a fallback, and a text that cannot be read at all is set aside instead of being overwritten. If this ever
/// grows, this class is the only thing to replace.
/// </summary>
public sealed class PlaceStore
{
	private const string RecentsKey = "places.recents";
	private const string FavouritesKey = "places.favourites";
	private const string RoutesKey = "places.routes";
	private const string HomeKey = "places.home";
	private const string SavedRoutesKey = "places.savedRoutes";
	private const int MaxRecents = 8;
	private const string PreviousSuffix = ".prev";
	private const string CorruptSuffix = ".corrupt";

	/// <summary>Provider that stored stops without a provider id belong to (the app only knew VVO then).</summary>
	private const string LegacyProviderId = VvoProviderInfo.Id;

	/// <summary>How many searched connections are kept. Older ones drop off the end.</summary>
	public const int MaxRoutes = 50;

	/// <summary>How many named connections are kept.</summary>
	public const int MaxSavedRoutes = 20;

	private readonly IKeyValueStore _store;
	private readonly object _gate = new();
	private readonly List<Location> _recents;
	private readonly List<Location> _favourites;
	private readonly List<RoutePair> _routes;
	private readonly List<SavedRoute> _savedRoutes;
	private Location? _home;
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
		: this(new PreferencesKeyValueStore())
	{
	}


	public PlaceStore(
		IKeyValueStore store)
	{
		ArgumentNullException.ThrowIfNull(store);

		_store = store;

		_recents =
			Load(RecentsKey);

		_favourites =
			Load(FavouritesKey);

		_routes =
			LoadRoutes();

		_savedRoutes =
			LoadSavedRoutes();

		_home =
			Load(HomeKey).FirstOrDefault();
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


	/// <summary>The place the passenger calls home, if one was set.</summary>
	public Location? Home
	{
		get
		{
			lock (_gate)
			{
				return _home;
			}
		}
	}


	/// <summary>Named connections, newest first.</summary>
	public IReadOnlyList<SavedRoute> SavedRoutes
	{
		get
		{
			lock (_gate)
			{
				return _savedRoutes.ToArray();
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


	public void SetHome(
		Location place)
	{
		ArgumentNullException.ThrowIfNull(
			place);

		lock (_gate)
		{
			_home = place;

			Save(
				HomeKey,
				[place]);
		}

		RaiseChanged();
	}


	public void ClearHome()
	{
		lock (_gate)
		{
			if (_home is null)
			{
				return;
			}

			_home = null;

			Save(
				HomeKey,
				[]);
		}

		RaiseChanged();
	}


	/// <summary>Keeps a named connection; a connection with the same name is replaced.</summary>
	public void AddSavedRoute(
		SavedRoute route)
	{
		ArgumentNullException.ThrowIfNull(
			route);

		lock (_gate)
		{
			_savedRoutes.RemoveAll(
				r => SameName(r, route));

			_savedRoutes.Insert(
				0,
				route);

			if (_savedRoutes.Count > MaxSavedRoutes)
			{
				_savedRoutes.RemoveRange(
					MaxSavedRoutes,
					_savedRoutes.Count - MaxSavedRoutes);
			}

			SaveSavedRoutes();
		}

		RaiseChanged();
	}


	public bool HasSavedRoute(
		string name)
	{
		lock (_gate)
		{
			return _savedRoutes.Any(
				r => string.Equals(
					r.Name,
					name,
					StringComparison.OrdinalIgnoreCase));
		}
	}


	public void RemoveSavedRoute(
		SavedRoute route)
	{
		ArgumentNullException.ThrowIfNull(
			route);

		lock (_gate)
		{
			if (_savedRoutes.RemoveAll(
				r => SameName(r, route)) == 0)
			{
				return;
			}

			SaveSavedRoutes();
		}

		RaiseChanged();
	}


	private static bool SameName(
		SavedRoute a,
		SavedRoute b) =>
		string.Equals(
			a.Name,
			b.Name,
			StringComparison.OrdinalIgnoreCase);


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


	/// <summary>
	/// Rewrites every stored list in the current format (readable entries only). Part of the storage
	/// upgrade; lists that are not stored are left alone.
	/// </summary>
	public void Normalize()
	{
		lock (_gate)
		{
			if (!string.IsNullOrWhiteSpace(_store.Get(RecentsKey)))
			{
				Save(RecentsKey, _recents);
			}

			if (!string.IsNullOrWhiteSpace(_store.Get(FavouritesKey)))
			{
				Save(FavouritesKey, _favourites);
			}

			if (!string.IsNullOrWhiteSpace(_store.Get(RoutesKey)))
			{
				SaveRoutes();
			}

			if (!string.IsNullOrWhiteSpace(_store.Get(SavedRoutesKey)))
			{
				SaveSavedRoutes();
			}

			if (_home is not null)
			{
				Save(HomeKey, [_home]);
			}
		}
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
	// Property names stay PascalCase, as the first releases wrote them.
	private sealed record Entry(
		string? Id,
		string Name,
		string? Place,
		double? Latitude,
		double? Longitude,
		string? ProviderId = null,
		string? Kind = null);


	/// <summary>Stored shape of a connection: the two endpoints, nothing else.</summary>
	private sealed record RouteEntry(
		Entry? From,
		Entry? To);


	/// <summary>Stored shape of a named connection.</summary>
	private sealed record SavedRouteEntry(
		string Name,
		Entry? From,
		Entry? To);


	private static Entry? ParseEntry(
		JsonElement element)
	{
		string? name =
			StoredJson.String(element, "Name");

		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		return new Entry(
			StoredJson.String(element, "Id"),
			name,
			StoredJson.String(element, "Place"),
			StoredJson.Number(element, "Latitude"),
			StoredJson.Number(element, "Longitude"),
			StoredJson.String(element, "ProviderId"),
			StoredJson.String(element, "Kind"));
	}


	private static RouteEntry? ParseRoute(
		JsonElement element)
	{
		if (!StoredJson.TryGet(element, "From", out JsonElement from)
			|| !StoredJson.TryGet(element, "To", out JsonElement to)
			|| ParseEntry(from) is not { } fromEntry
			|| ParseEntry(to) is not { } toEntry)
		{
			return null;
		}

		return new RouteEntry(fromEntry, toEntry);
	}


	private static SavedRouteEntry? ParseSavedRoute(
		JsonElement element)
	{
		if (StoredJson.String(element, "Name") is not { } name
			|| string.IsNullOrWhiteSpace(name)
			|| ParseRoute(element) is not { } route)
		{
			return null;
		}

		return new SavedRouteEntry(name, route.From, route.To);
	}


	private List<SavedRoute> LoadSavedRoutes() =>
		LoadList(SavedRoutesKey, ParseSavedRoute)
			.Select(
				entry =>
					new SavedRoute(
						entry.Name,
						ToLocation(entry.From!),
						ToLocation(entry.To!)))
			.Take(MaxSavedRoutes)
			.ToList();


	private void SaveSavedRoutes() =>
		SaveList(
			SavedRoutesKey,
			_savedRoutes.Select(
				route =>
					new SavedRouteEntry(
						route.Name,
						ToEntry(route.From),
						ToEntry(route.To))));


	private List<RoutePair> LoadRoutes() =>
		LoadList(RoutesKey, ParseRoute)
			.Select(
				entry =>
					new RoutePair(
						ToLocation(entry.From!),
						ToLocation(entry.To!)))
			.Take(MaxRoutes)
			.ToList();


	private void SaveRoutes() =>
		SaveList(
			RoutesKey,
			_routes.Select(
				route =>
					new RouteEntry(
						ToEntry(route.From),
						ToEntry(route.To))));


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
				: place.ProviderId,
			place.Kind == PlaceKind.Stop
				? null
				: place.Kind.ToString());


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
			Kind =
				Enum.TryParse(entry.Kind, out PlaceKind kind)
					? kind
					: PlaceKind.Stop,
			Name = entry.Name,
			Place = entry.Place,
			Latitude = entry.Latitude,
			Longitude = entry.Longitude
		};


	private List<Location> Load(
		string key) =>
		LoadList(key, ParseEntry)
			.Select(ToLocation)
			.ToList();


	private void Save(
		string key,
		List<Location> places) =>
		SaveList(
			key,
			places.Select(ToEntry));


	/// <summary>
	/// Reads a stored list: the text itself, else the fallback copy of the write before it. A text that
	/// cannot be understood at all is set aside under "&lt;key&gt;.corrupt" (kept for diagnosis, never read).
	/// </summary>
	private List<T> LoadList<T>(
		string key,
		Func<JsonElement, T?> parse)
		where T : class
	{
		foreach (string candidate in new[] { key, key + PreviousSuffix })
		{
			try
			{
				string? text =
					_store.Get(candidate);

				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}

				StoredRead<T> read =
					StoredJson.Read(text, parse);

				if (!read.Recognized)
				{
					_store.Set(candidate + CorruptSuffix, text);
					_store.Remove(candidate);

					continue;
				}

				if (read.Dropped > 0)
				{
					System.Diagnostics.Debug.WriteLine(
						$"PlaceStore '{candidate}': {read.Dropped} unreadable entries skipped");
				}

				if (read.Items.Count > 0 || candidate == key)
				{
					return read.Items.ToList();
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine(
					$"PlaceStore load '{candidate}' failed: {ex.Message}");
			}
		}

		return [];
	}


	private void SaveList<T>(
		string key,
		IEnumerable<T> entries)
	{
		try
		{
			string? previous =
				_store.Get(key);

			if (!string.IsNullOrWhiteSpace(previous))
			{
				_store.Set(key + PreviousSuffix, previous);
			}

			_store.Set(
				key,
				StoredJson.Write(entries));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"PlaceStore save '{key}' failed: {ex.Message}");
		}
	}
}
