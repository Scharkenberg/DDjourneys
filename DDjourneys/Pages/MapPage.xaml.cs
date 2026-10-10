using System.Globalization;
using DDjourneys.Controls;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Mapping;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>
/// A full-screen map. With a scene (journey, run) it shows that; without one it explores. Every map shows the stops
/// of the area in view (loaded again after each pan or zoom) and a tapped stop offers departures and a journey
/// to or from it. In pick mode (<see cref="Routes.MapModePick"/>) a tap answers the place search that opened it.
/// </summary>
public partial class MapPage : PanePage, IQueryAttributable
{
	/// <summary>Below this zoom the stops are too many to show; the page asks to zoom in.</summary>
	private const double MinStopZoom = 13;

	/// <summary>A stop closer than this to a scene marker is the same stop (the scene already draws it).</summary>
	private const double SameStopMeters = 30;

	private static readonly TimeSpan LocateWait = TimeSpan.FromSeconds(6);

	private readonly StopAreaService _areas;
	private readonly DeviceLocator _locator;
	private readonly LocationService _locations;
	private readonly ProviderRegistry _providers;
	private readonly PlaceStore _store;
	private readonly PlannerLauncher _planner;
	private readonly NetworkService _network;
	private readonly ParkingService _parking;
	private readonly SharedMobilityService _bikes;
	private readonly AppSettings _settings;

	private const string StopsLayer = "stops";
	private const string ZonesLayer = "zones";
	private const string ParkingLayer = "parking";
	private const string BikesLayer = "bikes";
	private const string VehiclesLayer = "vehicles";

	/// <summary>The map layer panel's link row for the network map (a reserved key, see <see cref="Routes.NetworkMapLayer"/>).</summary>
	private const string NetworkMapRow = Routes.NetworkMapLayer;

	private IReadOnlyList<TariffZoneShape> _zoneShapes = [];
	private bool _zonesPrepared;

	private readonly Dictionary<string, ParkingSite> _parkingSites = [];
	private string? _parkingSent;

	private readonly Dictionary<string, SharedStation> _bikeStations = [];
	private string? _bikesSent;

	private readonly LiveVehicleLayer _vehicleLayer;
	private IDispatcherTimer? _vehicleTimer;

	private readonly Dictionary<string, Location> _stops = [];
	private MapScene _scene = MapScene.Empty;
	private CancellationTokenSource? _load;
	private string? _target;
	private bool _isFrom;
	private bool _pick;
	private bool _busy;
	private bool _centered;
	private bool _wantsExplore;
	private Location? _at;
	private bool _exploring;
	private MapViewport? _viewport;
	private Dictionary<string, object>? _query;

	public MapPage(
		StopAreaService areas,
		DeviceLocator locator,
		LocationService locations,
		ProviderRegistry providers,
		PlaceStore store,
		PlannerLauncher planner,
		NetworkService network,
		ParkingService parking,
		SharedMobilityService bikes,
		VehicleStreamHub vehicleHub,
		AppSettings settings)
	{
		InitializeComponent();
		Motion.Prepare(this);

		_areas = areas;
		_locator = locator;
		_locations = locations;
		_providers = providers;
		_store = store;
		_planner = planner;
		_network = network;
		_parking = parking;
		_bikes = bikes;
		_vehicleLayer = new LiveVehicleLayer(vehicleHub);
		_settings = settings;

		Map.ViewportChanged += OnViewportChanged;
		Map.MarkerTapped += OnMarkerTapped;
		Map.PointTapped += OnPointTapped;
		Map.LayersChanged += OnLayersChanged;
	}

	private static ExtrasStrings Strings =>
		LocalizationService.Current.CurrentStrings.Extras;

	/// <summary>The scene, title and mode live in the page: a new instance gets the same query.</summary>
	protected internal override IDictionary<string, object>? RecreationQuery => _query;

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		_query = new Dictionary<string, object>(query);

		if (query.TryGetValue(Routes.MapTitle, out object? title)
			&& title is string text
			&& text.Length > 0)
		{
			Title = text;
		}

		if (query.TryGetValue(Routes.MapAt, out object? at)
			&& at is Location centre)
		{
			_at = centre;
		}

		if (query.TryGetValue(Routes.MapMode, out object? mode)
			&& mode is Routes.MapModePick)
		{
			_pick = true;
			_isFrom = query.TryGetValue(Routes.TargetIsFrom, out object? from) && from is true;
			_target = query.TryGetValue(Routes.Target, out object? target) ? target as string : null;

			Title =
				_target == Routes.TargetDepartures
					? Strings.MapPickStop
					: _isFrom
						? Strings.MapPickStart
						: Strings.MapPickEnd;
		}

		if (query.TryGetValue(Routes.MapScene, out object? value)
			&& value is MapScene scene)
		{
			_scene = scene;
			_ = Map.ShowAsync(scene);
		}
		else
		{
			// Without a usable CARTO key there is no map (the map view says so): nothing is located or loaded for it.
			_wantsExplore = true;
			TryStartExploring();
		}

		UpdateHint(null);
	}

	private void TryStartExploring()
	{
		if (!_wantsExplore
			|| _exploring
			|| !MapAvailability.IsAvailable)
		{
			return;
		}

		_exploring = true;

		_ = StartExploringAsync();
	}

	private void OnAvailabilityChanged(object? sender, EventArgs e) =>
		Dispatcher.Dispatch(TryStartExploring);

	protected override void OnAppearing()
	{
		base.OnAppearing();
		Motion.EnterPage(this);

		MapAvailability.Changed += OnAvailabilityChanged;
		Theme.Changed += OnThemeChanged;
		_providers.SelectionChanged += OnProviderChanged;
		AppVisibility.Changed += OnVehicleWindowVisibility;
		TryStartExploring();
		_ = PrepareLayersAsync();

		// The live vehicles layer ticks like the Vehicles page: only while it is on, seen and in view.
		_vehicleTimer ??= Dispatcher.CreateTimer();
		_vehicleTimer.Interval = TimeSpan.FromSeconds(2);
		_vehicleTimer.Tick += OnVehicleTick;

		if (_settings.MapVehicles
				&& IsOwnVisible)
		{
			_vehicleLayer.Start();
			_vehicleTimer.Start();
		}
	}

	protected override void OnDisappearing()
	{
		MapAvailability.Changed -= OnAvailabilityChanged;
		Theme.Changed -= OnThemeChanged;
		_providers.SelectionChanged -= OnProviderChanged;
		AppVisibility.Changed -= OnVehicleWindowVisibility;
		_load?.Cancel();
		_vehicleTimer?.Stop();
		_vehicleLayer.Pause();
		base.OnDisappearing();
	}

	/// <summary>Centre: the device position, else the last stop viewed, else the central city of the provider.</summary>
	private async Task StartExploringAsync()
	{
		(double latitude, double longitude, int zoom, bool me) = await FindCentreAsync();

		var markers = new List<MapMarker>();

		if (me)
		{
			markers.Add(new MapMarker("me", latitude, longitude, string.Empty, MapMarkerKind.Me));
		}

		_scene = new MapScene { Markers = markers, Fit = false };
		_centered = true;

		await Map.ShowAsync(_scene);
		await Map.SetModeAsync(explore: true, pick: _pick, latitude, longitude, zoom);
	}

	private async Task<(double Latitude, double Longitude, int Zoom, bool Me)> FindCentreAsync()
	{
		// Asked for a place (by another app, or a place the user is looking at): there first.
		if (_at is { Latitude: { } atLat, Longitude: { } atLon })
		{
			return (atLat, atLon, 16, false);
		}

		try
		{
			using var cancel = new CancellationTokenSource(LocateWait);

			if (await _locator.LocateAsync(cancel.Token) is { } here)
			{
				return (here.Latitude, here.Longitude, 16, true);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] position for the map failed: {ex.Message}");
		}

		if (_store.Recents.FirstOrDefault(
				stop => stop.IsStation
					&& stop.Latitude is not null
					&& stop.Longitude is not null) is { } last)
		{
			return (last.Latitude!.Value, last.Longitude!.Value, 16, false);
		}

		if (_providers.Selected?.Center is { } city)
		{
			return (city.Latitude, city.Longitude, city.Zoom, false);
		}

		return (51.0504, 13.7373, 12, false);
	}

	private async void OnViewportChanged(object? sender, MapViewport viewport)
	{
		_viewport = viewport;

		UpdateHint(viewport);

		// A viewport change never re-sends the layer itself; it only looks whether the cached
		// counts are stale, which the service turns into one fetch per two minutes at most.
	if (_settings.MapParking)
		{
			_ = SendParkingAsync();
		}

		if (_settings.MapBikes)
		{
			_ = SendBikesAsync();
		}

		_load?.Cancel();

		if (viewport.Zoom < MinStopZoom)
		{
			_stops.Clear();
			await Map.SetLayerAsync(StopsLayer, MapScene.Empty);

			return;
		}

		var cancel = new CancellationTokenSource();
		_load = cancel;

		try
		{
			IReadOnlyList<NearbyStop> found =
				await _areas.GetStopsAsync(viewport.Latitude, viewport.Longitude, viewport.RadiusMeters(), cancel.Token);

			if (cancel.IsCancellationRequested)
			{
				return;
			}

			_stops.Clear();

			var markers = new List<MapMarker>();

			foreach (NearbyStop nearby in found)
			{
				Location stop = nearby.Stop;

				if (stop.Latitude is not { } lat
					|| stop.Longitude is not { } lon
					|| IsInScene(lat, lon))
				{
					continue;
				}

				string id = stop.StopKey ?? stop.Id ?? $"{lat:F5},{lon:F5}";

				if (_stops.TryAdd(id, stop))
				{
					markers.Add(new MapMarker(id, lat, lon, string.Empty, MapMarkerKind.Stop, Title: stop.Name));
				}
			}

			await Map.SetLayerAsync(StopsLayer, new MapScene { Markers = markers, Fit = false });
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] stops for the view failed: {ex.Message}");
		}
	}

	private void OnThemeChanged(object? sender, EventArgs e) =>
			Dispatcher.Dispatch(
					() =>
					{
						SendZones();
						_ = SendParkingAsync();
						_ = SendBikesAsync();
						_vehicleLayer.MarkDirty();
					});

	/// <summary>Another provider: its stops are already gone with the viewport load; the zones layer starts over.</summary>
	private void OnProviderChanged(object? sender, string providerId) =>
			Dispatcher.Dispatch(
					() =>
					{
						_zoneShapes = [];
						_zonesPrepared = false;
						_ = Map.SetLayerAsync(ZonesLayer, MapScene.Empty);
						_ = PrepareLayersAsync();
					});

	private void OnLayersChanged(object? sender, IReadOnlyDictionary<string, bool> states)
	{
		if (states.TryGetValue(ZonesLayer, out bool zones))
		{
				_settings.MapZones = zones;

				SendZones();
			}

		if (states.TryGetValue(ParkingLayer, out bool parking))
		{
			_settings.MapParking = parking;
			_parkingSent = null;
			_ = SendParkingAsync();
		}

		if (states.TryGetValue(BikesLayer, out bool bikes))
		{
			_settings.MapBikes = bikes;
			_bikesSent = null;
			_ = SendBikesAsync();
		}

		if (states.TryGetValue(VehiclesLayer, out bool vehicles))
		{
			_settings.MapVehicles = vehicles;

			if (vehicles)
			{
				_vehicleLayer.Start();
				_vehicleTimer?.Start();
			}
			else
			{
				_vehicleLayer.Reset();
				_vehicleLayer.Pause();
				_vehicleTimer?.Stop();
				_ = Map.SetLayerAsync(VehiclesLayer, MapScene.Empty);
			}
		}

		if (states.TryGetValue(NetworkMapRow, out bool networkMap)
			&& networkMap)
		{
			_ = Shell.Current.GoToAsync(Routes.NetworkMap);
		}
	}

	/// <summary>
	/// The layer panel: the rows that do not depend on the provider's answer (the network map link, park &amp;
	/// ride) go out at once, so a slow or failed zones answer cannot hide them; the zones row joins when the
	/// shapes are known, and only where the provider publishes zone outlines at all.
	/// </summary>
	private async Task PrepareLayersAsync()
	{
		if (_zonesPrepared
						|| !_wantsExplore)
		{
			return;
		}

		_zonesPrepared = true;

		var rows = new List<MapView.MapLayerRow>
		{
			new(NetworkMapRow, Strings.NetworkMap, Checked: false, Link: true),
			new(ParkingLayer, Strings.MapParking, _settings.MapParking),
			new(BikesLayer, Strings.MapBikes, _settings.MapBikes),
			new(VehiclesLayer, Strings.MapVehicles, _settings.MapVehicles)
		};

		await Map.SetLayersAsync(Strings.MapLayers, rows);

		_ = SendParkingAsync();
		_ = SendBikesAsync();

		try
		{
			_zoneShapes = await _network.GetTariffZonesAsync();
		}
		catch (Exception ex)
		{
			_zonesPrepared = false;

			DiagnosticLog.Write($"[Map] tariff zones failed: {ex.Message}");

			return;
		}

		if (_zoneShapes.Count > 0)
		{
			rows.Add(new MapView.MapLayerRow(ZonesLayer, Strings.MapZones, _settings.MapZones));

			await Map.SetLayersAsync(Strings.MapLayers, rows);
		}

		SendZones();
	}

	/// <summary>The zones layer: sent when the switch is on, cleared when it is off - never on pan or zoom.</summary>
	private void SendZones()
	{
		if (!_zonesPrepared
				|| _zoneShapes.Count == 0)
		{
			return;
		}

		if (!_settings.MapZones)
		{
			_ = Map.SetLayerAsync(ZonesLayer, MapScene.Empty);

			return;
		}

		bool dark = Theme.IsDark;

		MapScene zones =
				new()
				{
					Polygons = [.. _zoneShapes.Select(shape => MapScenes.ZonePolygon(shape, dark))],
						Fit = false
				};

		string size = zones.ToJson(dark, MapScenes.Resolve);

		// A defensive cap: the shapes are simplified, but a surprise on the wire should be seen, not felt.
		if (size.Length > 150_000)
		{
			DiagnosticLog.Write($"[Map] tariff zones payload {size.Length} chars is too large; halving the point cap once");

			zones = new MapScene { Polygons = [.. _zoneShapes.Select(shape => MapScenes.ZonePolygon(shape, dark, 128))], Fit = false };
		}

		_ = Map.SetLayerAsync(ZonesLayer, zones);
	}

	/// <summary>
	/// The park &amp; ride layer: the whole list in one send, when the switch turns on, when the counts changed
	/// or the theme did - never per pan or zoom (the service's two-minute cache turns a viewport change into one
	/// background fetch per two minutes at most).
	/// </summary>
	private async Task SendParkingAsync()
	{
		if (!_wantsExplore)
		{
			return;
		}

		if (!_settings.MapParking)
		{
			_parkingSites.Clear();
			_parkingSent = null;

			_ = Map.SetLayerAsync(ParkingLayer, MapScene.Empty);

			return;
		}

		try
		{
			IReadOnlyList<ParkingSite> sites = await _parking.GetSitesAsync();

			ExtrasStrings strings = Strings;

			MapScene layer =
				new()
				{
					Fit = false,
					Markers =
						(List<MapMarker>)[.. sites.Select(site => new MapMarker(
							$"p:{site.Id}",
							site.Lat,
							site.Lon,
							site.Total > 0
								? string.Format(CultureInfo.CurrentCulture, strings.ParkingShort, site.Free)
								: site.Name,
							MapMarkerKind.Parking,
							MapScenes.ParkingColor(site),
							site.Total > 0
								? $"{site.Name} · {string.Format(CultureInfo.CurrentCulture, strings.ParkingCounts, site.Free, site.Total)}"
								: site.Name,
							site.LiveAt is { } liveAt
								? string.Format(CultureInfo.CurrentCulture, strings.ParkingUpdated, AgeText(liveAt))
								: null))]
				};

			string json = layer.ToJson(Theme.IsDark, MapScenes.Resolve);

			// The same counts in the same theme need no second send.
			if (json == _parkingSent)
			{
				return;
			}

			_parkingSent = json;
			_parkingSites.Clear();

			foreach (ParkingSite site in sites)
			{
				_parkingSites[$"p:{site.Id}"] = site;
		}

			await Map.SetLayerAsync(ParkingLayer, layer);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] park &amp; ride failed: {ex.Message}");
		}
	}

	/// <summary>How long ago a live count was taken, as text for the info line.</summary>
	private static string AgeText(DateTimeOffset at)
	{
		ExtrasStrings strings = Strings;
		double minutes = Math.Max(0, (DateTimeOffset.UtcNow - at).TotalMinutes);

		return minutes < 90
			? string.Format(CultureInfo.CurrentCulture, strings.LiveMinutesAgo, (int)Math.Round(minutes))
			: string.Format(CultureInfo.CurrentCulture, strings.LiveHoursAgo, (int)Math.Round(minutes / 60));
	}

	/// <summary>
	/// The two things to do at a park &amp; ride site: the departures of the stop nearest to it, or a journey that
	/// starts there (the planner resolves the position, so it needs a place, not a bare coordinate).
	/// </summary>
	private async Task OfferParkingAsync(ParkingSite site)
	{
		ExtrasStrings strings = Strings;

		string title =
			site.Total > 0
				? $"{site.Name}: {string.Format(CultureInfo.CurrentCulture, strings.ParkingCounts, site.Free, site.Total)}"
					+ (site.LiveAt is { } liveAt
						? $" · {string.Format(CultureInfo.CurrentCulture, strings.ParkingUpdated, AgeText(liveAt))}"
						: string.Empty)
				: site.Name;

		string choice =
			await DisplayActionSheetAsync(
				title,
				LocalizationService.Current.CurrentStrings.Common.Cancel,
				null,
				strings.MapStopDepartures,
				strings.MapJourneyFromHere);

		if (choice == strings.MapStopDepartures)
		{
			Location? stop = await PlaceNearAsync(site.Lat, site.Lon);

			if (stop is null)
			{
				await NothingHereAsync();

				return;
			}

			await Shell.Current.GoToAsync(
				Routes.Departures,
				new ShellNavigationQueryParameters
				{
					[Routes.Stop] = stop
				});
		}
		else if (choice == strings.MapJourneyFromHere)
		{
			Location? from = await PlaceNearAsync(site.Lat, site.Lon);

			if (from is null)
			{
				await NothingHereAsync();

				return;
			}

			await PlannerLauncher.FromAsync(from);
		}
	}

	/// <summary>
	/// The shared-bikes layer: the whole list in one send, when the switch turns on, when the counts changed
	/// or the theme did - never per pan or zoom (the service keeps the skeleton for an hour and the counts for
	/// a minute at most); the page culls the markers to the viewport engine-side.
	/// </summary>
	private async Task SendBikesAsync()
	{
		if (!_wantsExplore)
		{
			return;
		}

		if (!_settings.MapBikes)
		{
			_bikeStations.Clear();
			_bikesSent = null;

			_ = Map.SetLayerAsync(BikesLayer, MapScene.Empty);

			return;
		}

		try
		{
			IReadOnlyList<SharedStation> stations = await _bikes.GetStationsAsync();

			MapScene layer =
				new()
				{
					Fit = false,
					Markers =
						(List<MapMarker>)[.. stations.Select(station => new MapMarker(
							$"b:{station.Operator}:{station.StationId}",
							station.Lat,
							station.Lon,
							string.Empty,
							MapMarkerKind.Bike,
							MapScenes.BikeColor(station),
							$"{station.Name} · {station.Bikes}",
							station.UpdatedAt is { } reported
								? string.Format(CultureInfo.CurrentCulture, Strings.ParkingUpdated, AgeText(reported))
								: null))]
				};

			string json = layer.ToJson(Theme.IsDark, MapScenes.Resolve);

			// The same counts in the same theme need no second send.
			if (json == _bikesSent)
			{
				return;
			}

			_bikesSent = json;
			_bikeStations.Clear();

			foreach (SharedStation station in stations)
			{
				_bikeStations[$"b:{station.Operator}:{station.StationId}"] = station;
		}

			await Map.SetLayerAsync(BikesLayer, layer);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] shared bikes failed: {ex.Message}");
		}
	}

	/// <summary>
	/// The things to do at a shared-bike station: a journey to it, or renting. The rental link opens the
	/// operator's app only when the app is installed - a deep link that dies without it is a dead end - and
	/// falls back to the web page, then to the operator's own site.
	/// </summary>
	private async Task OfferBikesAsync(SharedStation station)
	{
		ExtrasStrings strings = Strings;

		var parts = new List<string>
		{
			string.Format(CultureInfo.CurrentCulture, strings.BikesCounts, station.Bikes)
		};

		if (station.Docks is { } docks)
		{
			parts.Add(string.Format(CultureInfo.CurrentCulture, strings.BikesDocks, docks));
		}

		if (station.UpdatedAt is { } reported)
		{
			parts.Add(string.Format(CultureInfo.CurrentCulture, strings.ParkingUpdated, AgeText(reported)));
		}

		string title = $"{station.Operator} · {station.Name}: {string.Join(" · ", parts)}";

		Uri? app = station.AppUri;
		Uri? page = station.WebUri ?? station.Website;

		var buttons = new List<string> { strings.MapJourneyToHere };

		if (app is not null || page is not null)
		{
			buttons.Add(strings.BikesOpenApp);
		}

		string choice =
			await DisplayActionSheetAsync(
				title,
				LocalizationService.Current.CurrentStrings.Common.Cancel,
				null,
				[.. buttons]);

		if (choice == strings.MapJourneyToHere)
		{
			Location? to = await PlaceNearAsync(station.Lat, station.Lon);

			if (to is null)
			{
				await NothingHereAsync();

				return;
			}

			await _planner.ToAsync(to);
		}
		else if (choice == strings.BikesOpenApp)
		{
			Uri? open = null;

			if (app is not null
					&& await Launcher.Default.CanOpenAsync(app))
			{
				open = app;
			}
			else
			{
				open = page;
			}

			if (open is not null)
			{
				await Launcher.Default.OpenAsync(open);
			}
		}
	}

	/// <summary>The address at the position, else the stop nearest to it: what a journey from here starts at.</summary>
	private async Task<Location?> PlaceNearAsync(double latitude, double longitude)
	{
		Location? place = await _locations.ResolveAddressAsync(latitude, longitude, timeout: LocateWait);

		if (place is null)
		{
			IReadOnlyList<Location> near =
				await _locations.SearchByCoordinatesAsync(latitude, longitude, timeout: LocateWait);

			place = near.FirstOrDefault(candidate => candidate.IsStation) ?? (near.Count > 0 ? near[0] : null);
		}

		return place;
	}

	private Task NothingHereAsync() =>
		DisplayAlertAsync(
			Title,
			Strings.MapNothingHere,
			LocalizationService.Current.CurrentStrings.Common.Ok);

	private bool IsInScene(double latitude, double longitude) =>
		_scene.Markers.Any(
			marker => marker.Kind is MapMarkerKind.Stop or MapMarkerKind.Current or MapMarkerKind.Start or MapMarkerKind.End
				&& GeoMath.DistanceMeters(latitude, longitude, marker.Latitude, marker.Longitude) < SameStopMeters);

	private void UpdateHint(MapViewport? viewport)
	{
		string? text =
			viewport is { Zoom: < MinStopZoom }
				? Strings.MapZoomHint
				: _pick && _centered
					? Strings.MapPickHint
					: null;

		HintLabel.Text = text;
		Hint.IsVisible = text is not null;
	}

	/// <summary>A hidden or minimised window has no use for live positions: the stream pauses and resumes with it.</summary>
	private void OnVehicleWindowVisibility(object? sender, EventArgs e)
	{
		if (AppVisibility.IsShown)
		{
			_vehicleLayer.Start();
		}
		else
		{
			_vehicleLayer.Pause();
		}
	}

	/// <summary>A wide window can hide the map behind deeper panes of its own chain, without the page being left.</summary>
	protected override void OnOwnVisibility(bool shown)
	{
		base.OnOwnVisibility(shown);

		if (shown)
		{
			if (_settings.MapVehicles
					&& AppVisibility.IsShown)
			{
				_vehicleLayer.Start();
				_vehicleTimer?.Start();
			}
		}
		else
		{
			_vehicleLayer.Pause();
			_vehicleTimer?.Stop();
		}
	}

	/// <summary>The live vehicles layer's tick: publish when something new arrived and the map is looked at.</summary>
	private void OnVehicleTick(object? sender, EventArgs e)
	{
		if (!AppVisibility.IsShown
				|| !IsOwnVisible)
		{
			return;
		}

		if (_vehicleLayer.Publish(_viewport, Theme.IsDark) is { } scene)
		{
			_ = Map.SetLayerAsync(VehiclesLayer, scene);
		}
	}

	/// <summary>The one thing to do at a vehicle: open the live page for its line (per-vehicle course matching without a departure context is unreliable).</summary>
	private async Task OfferVehicleAsync(LiveVehicle vehicle)
	{
		ExtrasStrings strings = Strings;

		string delay =
			vehicle.Delay is null
				? strings.LiveOnTime
				: Format.Delay(vehicle.Delay) ?? strings.LiveOnTime;

		string title =
			$"{string.Format(CultureInfo.CurrentCulture, strings.LiveLine, vehicle.Line)} · "
			+ $"{string.Format(CultureInfo.CurrentCulture, strings.LiveRun, vehicle.Run)}: {delay} · "
			+ string.Format(CultureInfo.CurrentCulture, strings.VehicleSeen, AgeText(vehicle.Time));

		string choice =
			await DisplayActionSheetAsync(
				title,
				LocalizationService.Current.CurrentStrings.Common.Cancel,
				null,
				strings.VehicleFollowLine);

		if (choice == strings.VehicleFollowLine)
		{
			await Panes.GoToAsync(
				Routes.Vehicles,
				new ShellNavigationQueryParameters
				{
					[Routes.Line] = vehicle.Line.ToString(CultureInfo.InvariantCulture)
				},
				this);
		}
	}

	private async void OnMarkerTapped(object? sender, string id)
	{
		if (_busy)
		{
			return;
		}

		_busy = true;

		try
		{
			// In pick mode only a stop is an answer; everything else is just decoration.
			if (_pick)
			{
				if (_stops.TryGetValue(id, out Location? picked))
				{
					await ReturnAsync(picked);
				}

				return;
			}

			if (_stops.TryGetValue(id, out Location? stop))
			{
				await OfferAsync(stop);
			}
			else if (_parkingSites.TryGetValue(id, out ParkingSite? site))
			{
				await OfferParkingAsync(site);
			}
			else if (_bikeStations.TryGetValue(id, out SharedStation? station))
			{
				await OfferBikesAsync(station);
			}
			else if (_vehicleLayer.TryGet(id, out LiveVehicle vehicle))
			{
				await OfferVehicleAsync(vehicle);
			}
		}
		catch (Exception ex)
		{
		DiagnosticLog.Write($"[Map] marker tap failed: {ex}");
		}
		finally
		{
		_busy = false;
		}
	}

	/// <summary>The three things to do at a stop.</summary>
	private async Task OfferAsync(Location stop)
	{
		ExtrasStrings strings = Strings;

		string choice =
			await DisplayActionSheetAsync(
				stop.Name,
				LocalizationService.Current.CurrentStrings.Common.Cancel,
				null,
				strings.MapStopDepartures,
				strings.MapJourneyToHere,
				strings.MapJourneyFromHere);

		if (choice == strings.MapStopDepartures)
		{
			await Shell.Current.GoToAsync(
				Routes.Departures,
				new ShellNavigationQueryParameters
				{
					[Routes.Stop] = stop
				});
		}
		else if (choice == strings.MapJourneyToHere)
		{
			await _planner.ToAsync(stop);
		}
		else if (choice == strings.MapJourneyFromHere)
		{
			await PlannerLauncher.FromAsync(stop);
		}
	}

	/// <summary>Pick mode: a point on the map becomes an address (journeys) or the nearest stop, and is confirmed once.</summary>
	private async void OnPointTapped(object? sender, (double Latitude, double Longitude) point)
	{
		if (!_pick
			|| _busy)
		{
			return;
		}

		_busy = true;

		try
		{
			bool needsStop = _target == Routes.TargetDepartures;

			Location? place =
				needsStop
					? null
					: await _locations.ResolveAddressAsync(point.Latitude, point.Longitude, timeout: LocateWait);

			if (place is null)
			{
				IReadOnlyList<Location> near =
					await _locations.SearchByCoordinatesAsync(point.Latitude, point.Longitude, timeout: LocateWait);

				place = near.FirstOrDefault(stop => stop.IsStation) ?? (near.Count > 0 ? near[0] : null);
			}

			if (place is null)
			{
				await DisplayAlertAsync(
					Title,
					Strings.MapNothingHere,
					LocalizationService.Current.CurrentStrings.Common.Ok);

				return;
			}

			if (await DisplayAlertAsync(
				place.Name,
				place.Place,
				Strings.MapUsePlace,
				LocalizationService.Current.CurrentStrings.Common.Cancel))
			{
				await ReturnAsync(place);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] point tap failed: {ex}");
		}
		finally
		{
			_busy = false;
		}
	}

	private Task ReturnAsync(Location place) =>
		Shell.Current.GoToAsync(
			"..",
			new ShellNavigationQueryParameters
			{
				[Routes.SelectedPlace] = place,
				[Routes.TargetIsFrom] = _isFrom,
				[Routes.Target] = _target ?? string.Empty
			});
}
