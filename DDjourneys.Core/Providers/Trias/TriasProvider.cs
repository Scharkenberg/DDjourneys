using System.Collections.Concurrent;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Services;
using Location = DDjourneys.Core.Models.Location;
using System.Globalization;
using System.Xml.Linq;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// The VVO TRIAS interface as a provider: journeys (with fares and a choice of optimisation),
/// stops, addresses and points of interest, and the departure monitor.
/// </summary>
public sealed class TriasProvider :
	IJourneyProvider,
	ILocationProvider,
	IDepartureProvider,
	IStopAreaProvider,
	IProviderDescriptor
{
	private readonly TriasClient _client;

	/// <summary>Stop positions looked up so far by stop place (null: asked, no position known).</summary>
	private readonly ConcurrentDictionary<string, GeoPosition?> _positions = new(StringComparer.Ordinal);

	public ProviderInfo Info =>
		TriasProviderInfo.Value;

	public TriasProvider(TriasClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		_client = client;
	}

	private static TimeSpan Timeout(int seconds) =>
		TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60));

	private static bool IsOurs(Location place) =>
		string.IsNullOrWhiteSpace(place.ProviderId)
		|| string.Equals(place.ProviderId, TriasProviderInfo.Id, StringComparison.OrdinalIgnoreCase);

	private static bool IsUsable(Location place) =>
		!string.IsNullOrWhiteSpace(place.Id)
		|| (place.Latitude is not null && place.Longitude is not null);

	// ---------- Journeys ----------

	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (!IsOurs(query.From)
			|| !IsOurs(query.To)
			|| (query.Via is { } via && !IsOurs(via)))
		{
			return JourneyResult.NotSuitable("trias_endpoint_other_provider");
		}

		if (!IsUsable(query.From) || !IsUsable(query.To))
		{
			return JourneyResult.NotSuitable("trias_endpoint_missing_id");
		}

		try
		{
			XDocument response =
				await _client
					.SendAsync(
						TriasRequestKind.Trip,
						dialect => TriasRequests.Trip(query, dialect),
						cancellationToken,
						Timeout(query.TimeoutSeconds))
					.ConfigureAwait(false);

			IReadOnlyList<Journey> journeys =
				TriasMapper.MapJourneys(response, query.From, query.To);

			// The schema cannot say "no transfers" (InterchangeLimit is a positive integer).
			if (query.Routing.MaxTransfers == MaxTransfers.None)
			{
				journeys = [.. journeys.Where(journey => journey.Legs.Count <= 1)];
			}

			if (journeys.Count > 0)
			{
				return JourneyResult.Success(journeys);
			}

			if (TriasMapper.Error(response) is { } error
				&& !TriasMapper.IsNoResult(error.Code))
			{
				return JourneyResult.Failure(
					error.Code ?? "trias_error",
					error.Text);
			}

			return JourneyResult.Success([]);
		}
		catch (ApiException ex)
		{
			return JourneyResult.Failure(
				ex.IsTransient ? "trias_unreachable" : "trias_http_error",
				ex.Detail);
		}
		catch (InvalidOperationException ex)
		{
			return JourneyResult.Failure("trias_unreadable_response", ex.Message);
		}
	}

	// ---------- Places ----------

	public Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		SearchAsync(query, PlaceKinds.Stops, timeout, cancellationToken);

	public async Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		PlaceKinds kinds,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		kinds |= PlaceKinds.Stops;

		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.Locations,
					dialect => TriasRequests.LocationByName(query, kinds, 12, dialect),
					cancellationToken,
					timeout)
				.ConfigureAwait(false);

		return
			[.. TriasMapper
				.MapLocations(response)
				.Where(place => kinds.HasFlag(place.Kind.ToFlag()))];
	}

	public async Task<IReadOnlyList<Location>> SearchByCoordinatesAsync(
		double latitude,
		double longitude,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.Locations,
					dialect => TriasRequests.LocationByPosition(latitude, longitude, PlaceKinds.Stops, 10, dialect),
					cancellationToken,
					timeout)
				.ConfigureAwait(false);

		return
			[.. TriasMapper
				.MapLocations(response)
				.Where(place => place.Kind == PlaceKind.Stop)];
	}

	public async Task<Location?> ResolveAddressAsync(
		double latitude,
		double longitude,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.Locations,
					dialect => TriasRequests.LocationByPosition(latitude, longitude, PlaceKinds.Addresses, 3, dialect),
					cancellationToken,
					timeout)
				.ConfigureAwait(false);

		return TriasMapper
			.MapLocations(response)
			.FirstOrDefault(place => place.Kind == PlaceKind.Address);
	}

	// ---------- Stops in an area ----------

	/// <summary>How far apart the position queries of a wide view are (one answer is the nearest 40 stops, a few hundred metres in town).</summary>
	private const int GridSpacingMeters = 1500;

	/// <summary>Most cells one view may cost.</summary>
	private const int GridCellLimit = 36;

	private readonly TtlCache<List<Location>> _cells = new(TimeSpan.FromMinutes(10), 512);

	/// <summary>Until when the area restriction is not asked: this server answered it with nothing.</summary>
	private long _circleUselessUntil;

	private static readonly TimeSpan CircleRetry = TimeSpan.FromMinutes(10);

	/// <summary>
	/// The stops of the area a map shows. The primary way is the area restriction of the location request (a circle,
	/// TRIAS takes up to 2000 m); a server that answers it with nothing is remembered for ten minutes and, like a view
	/// wider than the circle, is covered by position queries on a grid over the view (each the nearest 40 stops of its
	/// cell), merged by stop. The answer is the whole view, never just the few stops around its centre.
	/// </summary>
	public async Task<IReadOnlyList<NearbyStop>> GetStopsAroundAsync(
		double latitude,
		double longitude,
		int radiusMeters,
		int limit,
		CancellationToken cancellationToken = default)
	{
		int radius = Math.Clamp(radiusMeters, 100, 10_000);

		var found = new Dictionary<string, Location>(StringComparer.Ordinal);

		void Add(IEnumerable<Location> places)
		{
			foreach (Location place in places)
			{
				if (place.Kind == PlaceKind.Stop
					&& place.Latitude is not null
					&& place.Longitude is not null)
				{
					string key = place.StopKey ?? place.Id ?? $"{place.Latitude:F5},{place.Longitude:F5}";

					found.TryAdd(key, place);
				}
			}
		}

		int circle = 0;
		bool asked = false;

		if (DateTime.UtcNow.Ticks >= Interlocked.Read(ref _circleUselessUntil))
		{
			asked = true;

			try
			{
				XDocument response =
					await _client
						.SendAsync(
							TriasRequestKind.Locations,
							dialect => TriasRequests.LocationByArea(latitude, longitude, Math.Min(radius, 2000), Math.Min(limit, 100), dialect),
							cancellationToken,
							Timeout(10))
						.ConfigureAwait(false);

				Add(TriasMapper.MapLocations(response));

				circle = found.Count;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				DiagnosticLog.Write($"[Stops] TRIAS area request failed: {ex.Message}");
			}

			if (circle == 0)
			{
				Interlocked.Exchange(ref _circleUselessUntil, DateTime.UtcNow.Add(CircleRetry).Ticks);

				DiagnosticLog.Write("[Stops] TRIAS answers the area restriction with nothing: position queries on a grid for the next 10 minutes");
			}
		}

		// The circle covers at most 2000 m: a wider view, and a server without the restriction, are covered by the grid.
		int cells = 0;

		if (circle == 0 || radius > 2000)
		{
			cells = await AddGridAsync(latitude, longitude, radius, Add, cancellationToken).ConfigureAwait(false);
		}

		DiagnosticLog.Write($"[Stops] TRIAS view {latitude:F5},{longitude:F5} radius {radius} m: area {(asked ? circle.ToString(CultureInfo.InvariantCulture) : "skipped")}, grid {cells} cells, {found.Count} stops");

		return
			[.. found.Values
				.Select(
					place => new NearbyStop
					{
						Stop = place,
						DistanceMeters = (int)Math.Round(GeoMath.DistanceMeters(latitude, longitude, place.Latitude!.Value, place.Longitude!.Value))
					})
				.Where(stop => stop.DistanceMeters <= radius * 1.5)
				.OrderBy(stop => stop.DistanceMeters)
				.Take(limit)];
	}

	/// <summary>
	/// The nearest stops of the centres of a grid over the box (a few queries at a time); returns how many cells the
	/// box touches. The grid is fixed to the map, not to the view: panning reuses the cells it already has (kept ten
	/// minutes, never an empty answer), only the cells that came into view are asked for.
	/// </summary>
	private async Task<int> AddGridAsync(
		double latitude,
		double longitude,
		int radius,
		Action<IEnumerable<Location>> add,
		CancellationToken cancellationToken)
	{
		const double MetresPerDegreeLatitude = 111_320d;

		double stepLatitude = GridSpacingMeters / MetresPerDegreeLatitude;

		// The longitude step follows the latitude band (whole degrees), so the grid does not shift while the view moves.
		double stepLongitude = GridSpacingMeters / (MetresPerDegreeLatitude * Math.Cos(Math.Round(latitude) * Math.PI / 180));

		double spanLatitude = radius / MetresPerDegreeLatitude;
		double spanLongitude = radius / (MetresPerDegreeLatitude * Math.Cos(latitude * Math.PI / 180));

		int firstRow = (int)Math.Floor((latitude - spanLatitude) / stepLatitude);
		int lastRow = (int)Math.Floor((latitude + spanLatitude) / stepLatitude);
		int firstColumn = (int)Math.Floor((longitude - spanLongitude) / stepLongitude);
		int lastColumn = (int)Math.Floor((longitude + spanLongitude) / stepLongitude);

		var cells = new List<(int Row, int Column, double Latitude, double Longitude, double Distance)>();

		for (int row = firstRow; row <= lastRow; row++)
		{
			for (int column = firstColumn; column <= lastColumn; column++)
			{
				double cellLatitude = (row + 0.5) * stepLatitude;
				double cellLongitude = (column + 0.5) * stepLongitude;

				cells.Add((row, column, cellLatitude, cellLongitude, GeoMath.DistanceMeters(latitude, longitude, cellLatitude, cellLongitude)));
			}
		}

		// A very wide view costs the cells nearest its centre, not a request storm.
		(int Row, int Column, double Latitude, double Longitude, double Distance)[] chosen =
			[.. cells.OrderBy(cell => cell.Distance).Take(GridCellLimit)];

		using var gate = new SemaphoreSlim(4);
		var answers = new List<Location>[chosen.Length];

		await Task.WhenAll(
			chosen.Select(
				async (cell, index) =>
				{
					string key = string.Create(CultureInfo.InvariantCulture, $"{cell.Row}:{cell.Column}");

					if (_cells.TryGet(key, out List<Location>? cached))
					{
						answers[index] = cached;

						return;
					}

					await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

					try
					{
						XDocument response =
							await _client
								.SendAsync(
									TriasRequestKind.Locations,
									dialect => TriasRequests.LocationByPosition(cell.Latitude, cell.Longitude, PlaceKinds.Stops, 40, dialect),
									cancellationToken,
									Timeout(10))
								.ConfigureAwait(false);

						List<Location> places = [.. TriasMapper.MapLocations(response)];

						if (places.Count > 0)
						{
							_cells.Set(key, places);
						}

						answers[index] = places;
					}
					catch (Exception ex) when (ex is not OperationCanceledException)
					{
						DiagnosticLog.Write($"[Stops] TRIAS grid cell {key} failed: {ex.Message}");

						answers[index] = [];
					}
					finally
					{
						gate.Release();
					}
				}))
			.ConfigureAwait(false);

		foreach (List<Location>? places in answers)
		{
			if (places is not null)
			{
				add(places);
			}
		}

		return chosen.Length;
	}

	// ---------- Departures ----------

	public async Task<DepartureBoard> GetDeparturesAsync(
		DepartureQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (!IsOurs(query.Stop) || !IsUsable(query.Stop))
		{
			return DepartureBoard.Empty;
		}

		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.StopEvent,
					dialect => TriasRequests.StopEvents(query, dialect),
					cancellationToken,
					Timeout(query.TimeoutSeconds))
				.ConfigureAwait(false);

		return TriasMapper.MapBoard(response, query.Stop, query.IsArrival);
	}

	public async Task<IReadOnlyList<RunStop>> GetRunAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default) =>
		(await GetRunDetailAsync(departure, timeoutSeconds, cancellationToken).ConfigureAwait(false)).Stops;

	public async Task<RunDetail> GetRunDetailAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default)
	{
		RunDetail detail =
			await LoadRunDetailAsync(departure, timeoutSeconds, cancellationToken).ConfigureAwait(false);

		return detail with
		{
			Stops = await WithPositionsAsync(detail.Stops, cancellationToken).ConfigureAwait(false)
		};
	}

	/// <summary>
	/// TRIAS calls name their stops without coordinates, so the map and the live matching had nothing to work with.
	/// The positions are looked up once per stop place (a few at a time) and kept for the session.
	/// </summary>
	private async Task<IReadOnlyList<RunStop>> WithPositionsAsync(
		IReadOnlyList<RunStop> stops,
		CancellationToken cancellationToken)
	{
		string[] missing =
			[.. stops
				.Where(stop => stop.Station.Latitude is null
					&& !string.IsNullOrWhiteSpace(stop.Station.Id)
					&& !_positions.ContainsKey(stop.Station.Id))
				.Select(stop => stop.Station.Id)
				.Distinct(StringComparer.Ordinal)];

		if (missing.Length > 0)
		{
			using var gate = new SemaphoreSlim(4);
			using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			limit.CancelAfter(TimeSpan.FromSeconds(12));

			await Task.WhenAll(
				missing.Select(
					async id =>
					{
						try
						{
							await gate.WaitAsync(limit.Token).ConfigureAwait(false);

							try
							{
								_positions[id] = await LookupPositionAsync(id, limit.Token).ConfigureAwait(false);
							}
							finally
							{
								gate.Release();
							}
						}
						catch (OperationCanceledException)
						{
						}
						catch (ApiException ex)
						{
							DiagnosticLog.Write($"[TRIAS] position of {id} failed: {ex.Detail}");
						}
						catch (InvalidOperationException ex)
						{
							DiagnosticLog.Write($"[TRIAS] position of {id} unreadable: {ex.Message}");
						}
					})).ConfigureAwait(false);
		}

		return
			[.. stops
				.Select(
					stop => stop.Station.Latitude is null
						&& _positions.TryGetValue(stop.Station.Id, out GeoPosition? position)
						&& position is not null
							? new RunStop
							{
								Station = Positioned(stop.Station, position),
								Position = stop.Position,
								Scheduled = stop.Scheduled,
								Realtime = stop.Realtime,
								State = stop.State,
								Occupancy = stop.Occupancy
							}
							: stop)];
	}

	private async Task<GeoPosition?> LookupPositionAsync(string id, CancellationToken cancellationToken)
	{
		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.Locations,
					dialect => TriasRequests.LocationByRef(id, dialect),
					cancellationToken,
					Timeout(8))
				.ConfigureAwait(false);

		Location? place =
			TriasMapper
				.MapLocations(response)
				.FirstOrDefault(item => item.Latitude is not null && item.Longitude is not null);

		return place is { Latitude: { } latitude, Longitude: { } longitude }
			? new GeoPosition { Latitude = latitude, Longitude = longitude }
			: null;
	}

	private static Station Positioned(Station station, GeoPosition position) =>
		new()
		{
			Id = station.Id,
			ProviderId = station.ProviderId,
			Name = station.Name,
			Place = station.Place,
			Platform = station.Platform,
			PlatformKind = station.PlatformKind,
			Latitude = position.Latitude,
			Longitude = position.Longitude
		};

	private async Task<RunDetail> LoadRunDetailAsync(
		Departure departure,
		int timeoutSeconds,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(departure);

		if (departure.ProviderData is not TriasRunData run)
		{
			return new RunDetail([]);
		}

		// What the stop event itself carried: the calls as they were then and the operating days.
		var known =
			new RunDetail(run.Stops)
			{
				OperatingDays = run.OperatingDays
			};

		if (run.JourneyRef is not { } journeyRef)
		{
			return known;
		}

		// TripInfo has the current estimates and, when the server knows it, where the vehicle is.
		string day =
			run.OperatingDayRef
			?? departure.Scheduled.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		try
		{
			XDocument response =
				await _client
					.SendAsync(
						TriasRequestKind.TripInfo,
						dialect => TriasRequests.TripInfo(journeyRef, day, dialect),
						cancellationToken,
						Timeout(timeoutSeconds))
					.ConfigureAwait(false);

			RunDetail detail = TriasMapper.MapRun(response, departure.StopId);

			if (detail.Stops.Count > 0)
			{
				return detail with
				{
					OperatingDays = detail.OperatingDays ?? run.OperatingDays
				};
			}
		}
		catch (ApiException ex)
		{
			DiagnosticLog.Write($"[TRIAS] TripInfo failed, using the stop event's run: {ex.Detail}");
		}
		catch (InvalidOperationException ex)
		{
			DiagnosticLog.Write($"[TRIAS] TripInfo unreadable, using the stop event's run: {ex.Message}");
		}

		return known;
	}
}
