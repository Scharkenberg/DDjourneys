using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;
using DDjourneys.Core.Providers.Vvo;
using Location = DDjourneys.Core.Models.Location;
using SavedLocation = DDjourneys.Core.Models.SavedLocation;
using SavedRoute = DDjourneys.Core.Models.SavedRoute;

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
	private const string SavedLocationsKey = "places.savedLocations";
	private const string SavedRoutesKey = "places.savedRoutes";
	private const int MaxRecents = 8;
	private const int MaxSavedLocations = 20;
	private const int MaxSavedRoutes = 20;
	private const string PreviousSuffix = ".prev";
	private const string CorruptSuffix = ".corrupt";

	/// <summary>Provider that stored stops without a provider id belong to (the app only knew VVO then).</summary>
	private const string LegacyProviderId = VvoProviderInfo.Id;

	/// <summary>How many searched connections are kept. Older ones drop off the end.</summary>
	public const int MaxRoutes = 50;

	private readonly IKeyValueStore _store;
	private readonly object _gate = new();
	private readonly List<Location> _recents;
	private readonly List<Location> _favourites;
	private readonly List<RoutePair> _routes;
	private readonly List<SavedLocation> _savedLocations;
	private readonly List<SavedRoute> _savedRoutes;
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

		_savedLocations =
			LoadSavedLocations();

		_savedRoutes =
			LoadSavedRoutes();
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
	/// Saved locations (home, work, etc.) in the order they were added.
	/// </summary>
	public IReadOnlyList<SavedLocation> SavedLocations
	{
		get
		{
			lock (_gate)
			{
				return _savedLocations.ToArray();
			}
		}
	}


	/// <summary>
	/// Returns the home location if one is set, otherwise null.
	/// </summary>
	public SavedLocation? Home
	{
		get
		{
			lock (_gate)
			{
				return _savedLocations.FirstOrDefault(loc => loc.IsHome);
			}
		}
	}


	/// <summary>
	/// Returns true if a home location is set.
	/// </summary>
	public bool HasHome => Home is not null;


	/// <summary>
	/// Saved route presets in the order they were added.
	/// </summary>
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


	/// <summary>
	/// Returns true if there are any saved routes.
	/// </summary>
	public bool HasSavedRoutes
	{
		get
		{
			lock (_gate)
			{
				return _savedRoutes.Count > 0;
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


	/// <summary>
	/// Sets the home location. If a home location already exists, it is replaced.
	/// </summary>
	public void SetHome(SavedLocation homeLocation)
	{
		ArgumentNullException.ThrowIfNull(homeLocation);

		lock (_gate)
		{
			// Remove existing home location if any
			_savedLocations.RemoveAll(loc => loc.IsHome);

			// Add or update the new home location
			var newHome = new SavedLocation(
				homeLocation.Name,
				homeLocation.Location,
				true);

			_savedLocations.RemoveAll(loc => loc.Name == homeLocation.Name);
			_savedLocations.Insert(0, newHome);

			if (_savedLocations.Count > MaxSavedLocations)
			{
				_savedLocations.RemoveRange(
					MaxSavedLocations,
					_savedLocations.Count - MaxSavedLocations);
			}

			SaveSavedLocations();
		}

		RaiseChanged();
	}


	/// <summary>
	/// Adds a saved location (non-home).
	/// </summary>
	public void AddSavedLocation(SavedLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		lock (_gate)
		{
			// Don't allow setting home this way
			if (location.IsHome)
			{
				return;
			}

			// Check if location with same name already exists
			_savedLocations.RemoveAll(loc => loc.Name == location.Name);

			_savedLocations.Insert(0, location);

			if (_savedLocations.Count > MaxSavedLocations)
			{
				_savedLocations.RemoveRange(
					MaxSavedLocations,
					_savedLocations.Count - MaxSavedLocations);
			}

			SaveSavedLocations();
		}

		RaiseChanged();
	}


	/// <summary>
	/// Removes a saved location.
	/// </summary>
	public void RemoveSavedLocation(SavedLocation location)
	{
		ArgumentNullException.ThrowIfNull(location);

		lock (_gate)
		{
			if (_savedLocations.RemoveAll(loc => loc.Name == location.Name) == 0)
			{
				return;
			}

			SaveSavedLocations();
		}

		RaiseChanged();
	}


	/// <summary>
	/// Adds a saved route preset.
	/// </summary>
	public void AddSavedRoute(SavedRoute route)
	{
		ArgumentNullException.ThrowIfNull(route);

		lock (_gate)
		{
			// Check if route with same name already exists
			_savedRoutes.RemoveAll(r => r.Name == route.Name);

			_savedRoutes.Insert(0, route);

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


	/// <summary>
	/// Removes a saved route preset.
	/// </summary>
	public void RemoveSavedRoute(SavedRoute route)
	{
		ArgumentNullException.ThrowIfNull(route);

		lock (_gate)
		{
			if (_savedRoutes.RemoveAll(r => r.Name == route.Name) == 0)
			{
				return;
			}

			SaveSavedRoutes();
		}

		RaiseChanged();
	}


	/// <summary>
	/// Clears the home location if set.
	/// </summary>
	public void ClearHome()
	{
		lock (_gate)
		{
			if (_savedLocations.RemoveAll(loc => loc.IsHome) == 0)
			{
				return;
			}

			SaveSavedLocations();
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

			if (!string.IsNullOrWhiteSpace(_store.Get(SavedLocationsKey)))
			{
				SaveSavedLocations();
			}

			if (!string.IsNullOrWhiteSpace(_store.Get(SavedRoutesKey)))
			{
				SaveSavedRoutes();
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


	// Saved location entry for serialization
	private sealed record SavedLocationEntry(
		string Name,
		Entry? Location,
		bool IsHome);


	// Saved route entry for serialization
	private sealed record SavedRouteEntry(
		string Name,
		Entry? From,
		Entry? To,
		int? MaxChanges,
		int? MaxDuration,
		bool? UseElevator,
		bool? UseEscalator,
		bool? UseSolidStairs,
		bool? UseMovingPlatform,
		int? WalkSpeed,
		int? MarginBefore,
		int? MarginAfter,
		DateTime? DefaultDateTime,
		bool IsDeparture);


	private static SavedLocationEntry? ParseSavedLocation(
		JsonElement element)
	{
		string? name = StoredJson.String(element, "Name");
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		bool isHome = StoredJson.Boolean(element, "IsHome");
		Entry? location = null;
		if (StoredJson.TryGet(element, "Location", out JsonElement locationElement))
		{
			location = ParseEntry(locationElement);
		}

		if (location is null)
		{
			return null;
		}

		return new SavedLocationEntry(name, location, isHome);
	}


	private static SavedRouteEntry? ParseSavedRoute(
		JsonElement element)
	{
		string? name = StoredJson.String(element, "Name");
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		Entry? from = null;
		Entry? to = null;

		if (StoredJson.TryGet(element, "From", out JsonElement fromElement))
		{
			from = ParseEntry(fromElement);
		}

		if (StoredJson.TryGet(element, "To", out JsonElement toElement))
		{
			to = ParseEntry(toElement);
		}

		if (from is null || to is null)
		{
			return null;
		}

		return new SavedRouteEntry(
			name,
			from,
			to,
			StoredJson.Int32(element, "MaxChanges"),
			StoredJson.Int32(element, "MaxDuration"),
			StoredJson.Boolean(element, "UseElevator"),
			StoredJson.Boolean(element, "UseEscalator"),
			StoredJson.Boolean(element, "UseSolidStairs"),
			StoredJson.Boolean(element, "UseMovingPlatform"),
			StoredJson.Int32(element, "WalkSpeed"),
			StoredJson.Int32(element, "MarginBefore"),
			StoredJson.Int32(element, "MarginAfter"),
			StoredJson.DateTime(element, "DefaultDateTime"),
			StoredJson.Boolean(element, "IsDeparture"));
	}


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
		string? ProviderId = null);


	/// <summary>Stored shape of a connection: the two endpoints, nothing else.</summary>
	private sealed record RouteEntry(
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
			StoredJson.String(element, "ProviderId"));
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


	private List<RoutePair> LoadRoutes() =>
		LoadList(RoutesKey, ParseRoute)
			.Select(
				entry =>
					new RoutePair(
						ToLocation(entry.From!),
						ToLocation(entry.To!)))
			.Take(MaxRoutes)
			.ToList();


	private List<SavedLocation> LoadSavedLocations() =>
		LoadList(SavedLocationsKey, ParseSavedLocation)
			.Select(
				entry =>
					new SavedLocation(
						entry.Name,
						ToLocation(entry.Location!),
						entry.IsHome))
			.Take(MaxSavedLocations)
			.ToList();


	private List<SavedRoute> LoadSavedRoutes() =>
		LoadList(SavedRoutesKey, ParseSavedRoute)
			.Select(
				entry =>
					new SavedRoute(
						entry.Name,
						ToLocation(entry.From!),
						ToLocation(entry.To!),
						new RoutingPreferences
						{
							MaxChanges = entry.MaxChanges,
							MaxDuration = entry.MaxDuration,
							Accessibility = new AccessibilityPreferences
							{
								UseElevator = entry.UseElevator,
								UseEscalator = entry.UseEscalator,
								UseSolidStairs = entry.UseSolidStairs,
								UseMovingPlatform = entry.UseMovingPlatform
							},
							WalkSpeed = entry.WalkSpeed,
							MarginBefore = entry.MarginBefore,
							MarginAfter = entry.MarginAfter
						},
						entry.DefaultDateTime,
						entry.IsDeparture))
			.Take(MaxSavedRoutes)
			.ToList();


	private void SaveRoutes() =>
		SaveList(
			RoutesKey,
			_routes.Select(
				route =>
					new RouteEntry(
						ToEntry(route.From),
						ToEntry(route.To))));


	private void SaveSavedLocations() =>
		SaveList(
			SavedLocationsKey,
			_savedLocations.Select(
				location =>
					new SavedLocationEntry(
						location.Name,
						ToEntry(location.Location),
						location.IsHome)));


	private void SaveSavedRoutes() =>
		SaveList(
			SavedRoutesKey,
			_savedRoutes.Select(
				route =>
					new SavedRouteEntry(
						route.Name,
						ToEntry(route.From),
						ToEntry(route.To),
						route.Routing.MaxChanges,
						route.Routing.MaxDuration,
						route.Routing.Accessibility?.UseElevator,
						route.Routing.Accessibility?.UseEscalator,
						route.Routing.Accessibility?.UseSolidStairs,
						route.Routing.Accessibility?.UseMovingPlatform,
						route.Routing.WalkSpeed,
						route.Routing.MarginBefore,
						route.Routing.MarginAfter,
						route.DefaultDateTime,
						route.IsDeparture)));


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
