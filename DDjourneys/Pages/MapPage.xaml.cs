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
public partial class MapPage : ContentPage, IQueryAttributable
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

	public MapPage(
		StopAreaService areas,
		DeviceLocator locator,
		LocationService locations,
		ProviderRegistry providers,
		PlaceStore store,
		PlannerLauncher planner)
	{
		InitializeComponent();
		Motion.Prepare(this);

		_areas = areas;
		_locator = locator;
		_locations = locations;
		_providers = providers;
		_store = store;
		_planner = planner;

		Map.ViewportChanged += OnViewportChanged;
		Map.MarkerTapped += OnMarkerTapped;
		Map.PointTapped += OnPointTapped;
	}

	private static ExtrasStrings Strings =>
		LocalizationService.Current.CurrentStrings.Extras;

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
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
		TryStartExploring();
	}

	protected override void OnDisappearing()
	{
		MapAvailability.Changed -= OnAvailabilityChanged;
		_load?.Cancel();
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

		_load?.Cancel();

		if (viewport.Zoom < MinStopZoom)
		{
			_stops.Clear();
			await Map.SetOverlayAsync(MapScene.Empty);

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

			await Map.SetOverlayAsync(new MapScene { Markers = markers, Fit = false });
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] stops for the view failed: {ex.Message}");
		}
	}

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

	private async void OnMarkerTapped(object? sender, string id)
	{
		if (_busy
			|| !_stops.TryGetValue(id, out Location? stop))
		{
			return;
		}

		_busy = true;

		try
		{
			if (_pick)
			{
				await ReturnAsync(stop);

				return;
			}

			await OfferAsync(stop);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] stop tap failed: {ex}");
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
