using DDjourneys.Core.Diagnostics;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Requests;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>
/// Departure monitor, route changes, stop lines, nearby stops and tariff zones of the VVO WebAPI
/// (<c>dm</c>, <c>dm/trip</c>, <c>rc</c>, <c>rc/lines</c>, <c>stt/lines</c>, <c>map/pins</c>, <c>map/polygons</c>).
/// Failures are thrown (<see cref="Api.ApiException"/>); a service that answers "no data" gives an empty result.
/// </summary>
public sealed class VvoNetworkProvider :
	IDepartureProvider,
	INetworkInfoProvider,
	IStopAreaProvider,
	IProviderDescriptor
{
	private static readonly TimeSpan MaxZoneAge = TimeSpan.FromHours(24);

	private readonly VvoApiClient _apiClient;
	private readonly SemaphoreSlim _zoneGate = new(1, 1);
	private IReadOnlyList<ZonePolygon>? _zones;
	private DateTimeOffset _zonesExpire;

	public VvoNetworkProvider(
		VvoApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(apiClient);

		_apiClient = apiClient;
	}

	/// <inheritdoc />
	public ProviderInfo Info =>
		VvoProviderInfo.Value;

	/// <inheritdoc />
	public async Task<DepartureBoard> GetDeparturesAsync(
		DepartureQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (string.IsNullOrWhiteSpace(query.Stop.Id))
		{
			return DepartureBoard.Empty;
		}

		VvoDepartureResponse? response =
			await _apiClient.GetDeparturesAsync(
				new VvoDepartureRequest
				{
					StopId = query.Stop.Id,
					Limit = Math.Clamp(query.Limit, 1, 60),
					Time = query.Time?.ToString("O", CultureInfo.InvariantCulture),
					IsArrival = query.IsArrival,
					ModesOfTransport = ModesOf(query.Modes)
				},
				Timeout(query.TimeoutSeconds),
				cancellationToken)
				.ConfigureAwait(false);

		return response is null
			? DepartureBoard.Empty
			: VvoNetworkMapper.MapBoard(response, query.Stop, query.IsArrival);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<RunStop>> GetRunAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default) =>
		(await GetRunDetailAsync(
				departure,
				timeoutSeconds,
				cancellationToken)
			.ConfigureAwait(false)).Stops;

	/// <inheritdoc />
	public async Task<RunDetail> GetRunDetailAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default)
	{
		VvoRunResponse? response =
			await GetRunResponseAsync(
					departure,
					timeoutSeconds,
					cancellationToken)
				.ConfigureAwait(false);

		return response is null
			? new RunDetail([])
			: new RunDetail(VvoNetworkMapper.MapRun(response))
			{
				Path = VvoPathMapper.MapRunPath(response)
			};
	}

	/// <summary>The run answer behind a departure, whichever of the request forms the service takes.</summary>
	private async Task<VvoRunResponse?> GetRunResponseAsync(
		Departure departure,
		int timeoutSeconds,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(departure);

		if (string.IsNullOrWhiteSpace(departure.StopId))
		{
			return null;
		}

		// tripid names the line's course, not one run (kiliankoe's webapi.md, "Run identity"): the run is the
		// next real-time departure at stopid at or after time. So the time is the run's own real-time
		// departure here, a minute early, and the answer is checked against its scheduled time.
		DateTimeOffset token =
			departure.Effective.AddSeconds(-60);

		DiagnosticLog.Write(
			$"[VVO run] departure trip '{departure.Id}' stop {departure.StopId} line {departure.Line.Name} "
			+ $"scheduled {departure.Scheduled:O} realtime {departure.Realtime:O} arrival {departure.IsArrival} "
			+ $"now {DateTimeOffset.UtcNow:O} token {token:O} ({(token < DateTimeOffset.UtcNow ? "past" : "future")})");

		bool Matches(VvoRunResponse candidate)
		{
			VvoRunStop? current =
				candidate.Stops.FirstOrDefault(
					stop => string.Equals(stop.Position, "Current", StringComparison.OrdinalIgnoreCase));

			if (current?.Time is not { } scheduled)
			{
				DiagnosticLog.Write($"[VVO run] no 'Current' stop with a time in the answer ({candidate.Stops.Count} stops); taken as is");

				return candidate.Stops.Count > 0;
			}

			bool same =
				Math.Abs((scheduled - departure.Scheduled).TotalSeconds) < 90;

			DiagnosticLog.Write(
				$"[VVO run] Current '{current.Name}' scheduled {scheduled:O}, wanted {departure.Scheduled:O}: {(same ? "same run" : "DIFFERENT run")}");

			return same;
		}

		VvoRunResponse? response =
			await _apiClient.GetDepartureRunAsync(
				_apiClient.BuildRunAttempts(
					departure.Id,
					departure.StopId,
					departure.IsArrival,
					token),
				Matches,
				Timeout(timeoutSeconds),
				cancellationToken)
				.ConfigureAwait(false);

		return response;
	}

	/// <inheritdoc />
	public async Task<DisruptionReport> GetDisruptionsAsync(
		bool shortTermOnly = false,
		CancellationToken cancellationToken = default)
	{
		VvoRouteChangesResponse? response =
			await _apiClient.GetRouteChangesAsync(
				new VvoRouteChangesRequest
				{
					ShortTerm = shortTermOnly
				},
				cancellationToken: cancellationToken)
				.ConfigureAwait(false);

		return response is null
			? DisruptionReport.Empty
			: VvoNetworkMapper.MapDisruptions(response);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<DisruptionLine>> GetDisruptedLinesAsync(
		CancellationToken cancellationToken = default)
	{
		VvoChangedLinesResponse? response =
			await _apiClient.GetChangedLinesAsync(cancellationToken: cancellationToken)
				.ConfigureAwait(false);

		return response is null
			? []
			: [.. response.Lines
				.Select(VvoNetworkMapper.MapLine)
				.OfType<DisruptionLine>()];
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<StopLine>> GetStopLinesAsync(
		Location stop,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(stop);

		if (string.IsNullOrWhiteSpace(stop.Id))
		{
			return [];
		}

		VvoStopLinesResponse? response =
			await _apiClient.GetStopLinesAsync(stop.Id, cancellationToken: cancellationToken)
				.ConfigureAwait(false);

		return response is null
			? []
			: VvoNetworkMapper.MapStopLines(response);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<NearbyStop>> GetStopsAroundAsync(
		double latitude,
		double longitude,
		int radiusMeters,
		int limit,
		CancellationToken cancellationToken = default) =>
		[.. (await GetNearbyStopsAsync(latitude, longitude, radiusMeters, cancellationToken).ConfigureAwait(false)).Take(limit)];

	/// <inheritdoc />
	public async Task<IReadOnlyList<NearbyStop>> GetNearbyStopsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 500,
		CancellationToken cancellationToken = default)
	{
		if (!VvoCoordinateConverter.TryToGk4(
			latitude,
			longitude,
			out (double Easting, double Northing) centre))
		{
			return [];
		}

		int radius = Math.Clamp(radiusMeters, 100, 2000);

		// GK4 is metric (scale 1 on the central meridian, about 1.0001 in Saxony): a box in metres is a box here.
		static string Metres(double value) =>
			Math.Round(value).ToString("F0", CultureInfo.InvariantCulture);

		VvoMapPinsResponse? response =
			await _apiClient.GetMapPinsAsync(
				new VvoMapPinsRequest
				{
					SouthWestLatitude = Metres(centre.Northing - radius),
					SouthWestLongitude = Metres(centre.Easting - radius),
					NorthEastLatitude = Metres(centre.Northing + radius),
					NorthEastLongitude = Metres(centre.Easting + radius),
					PinTypes = ["Stop"]
				},
				cancellationToken: cancellationToken)
				.ConfigureAwait(false);

		if (response is null)
		{
			return [];
		}

		// Pins are written like PointFinder entries; a stop pin has a numeric id.
		return
			[.. response.Pins
				.Select(VvoPoint.Parse)
				.Where(point => point.IsStop && !string.IsNullOrWhiteSpace(point.Name))
				.Select(VvoLocationProvider.Map)
				.Where(stop => stop.Latitude is not null && stop.Longitude is not null)
				.Select(
					stop =>
						new NearbyStop
						{
							Stop = stop,
							DistanceMeters =
								(int)Math.Round(
									GeoMath.DistanceMeters(
										latitude,
										longitude,
										stop.Latitude!.Value,
										stop.Longitude!.Value))
						})
				.Where(nearby => nearby.DistanceMeters <= radius * 1.25)
				.OrderBy(nearby => nearby.DistanceMeters)
				.Take(20)];
	}

	/// <inheritdoc />
	/// <inheritdoc />
	public async Task<IReadOnlyList<TariffZoneShape>> GetTariffZonesAsync(
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<ZonePolygon> zones =
			await LoadZonesAsync(cancellationToken)
				.ConfigureAwait(false);

		// Zone-number order: the same picture on every load, whatever the wire order was.
		return
			(IReadOnlyList<TariffZoneShape>)[.. zones
					.Select(ToShape)
					.OfType<TariffZoneShape>()
					.OrderBy(shape => shape.Number, StringComparer.OrdinalIgnoreCase)];
	}

	public async Task<TariffZone?> FindTariffZoneAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default)
	{
		if (!VvoCoordinateConverter.TryToGk4(
			latitude,
			longitude,
			out (double Easting, double Northing) point))
		{
			return null;
		}

		IReadOnlyList<ZonePolygon> zones =
			await LoadZonesAsync(cancellationToken)
				.ConfigureAwait(false);

		// Smaller zones first: inner zones may lie inside the area of an outer one.
		return zones
			.Where(zone => zone.Contains(point.Easting, point.Northing))
			.OrderBy(zone => zone.Area)
			.FirstOrDefault()
			?.Zone;
	}

	private async Task<IReadOnlyList<ZonePolygon>> LoadZonesAsync(
		CancellationToken cancellationToken)
	{
		if (_zones is { } cached
			&& DateTimeOffset.UtcNow < _zonesExpire)
		{
			return cached;
		}

		await _zoneGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (_zones is { } again
				&& DateTimeOffset.UtcNow < _zonesExpire)
			{
				return again;
			}

			VvoMapPolygonsResponse? response =
				await _apiClient.GetTariffPolygonsAsync(cancellationToken: cancellationToken)
					.ConfigureAwait(false);

			IReadOnlyList<ZonePolygon> zones =
				[.. (response?.Polygons ?? [])
					.Select(ZonePolygon.Parse)
					.OfType<ZonePolygon>()];

			DateTimeOffset now = DateTimeOffset.UtcNow;
			DateTimeOffset expires = response?.ExpirationTime ?? now + MaxZoneAge;

			_zones = zones;
			_zonesExpire = expires > now + MaxZoneAge || expires <= now
				? now + MaxZoneAge
				: expires;

			return zones;
		}
		finally
		{
			_zoneGate.Release();
		}
	}

	private static IReadOnlyList<string>? ModesOf(
		ModeFilter modes) =>
		modes is ModeFilter.None or ModeFilter.All
			? null
			: [.. Enum.GetValues<ModeFilter>()
				.Where(mode => mode is not ModeFilter.None and not ModeFilter.All && modes.HasFlag(mode))
				.Select(mode => mode.ToString())];

	private static TimeSpan Timeout(
		int seconds) =>
		TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60));

	/// <summary>A zone polygon as a map shape: the ring in WGS84, the centre as the label position.</summary>
	private static TariffZoneShape? ToShape(ZonePolygon zone)
	{
		if (!int.TryParse(
				zone.Zone.Number,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out int number))
		{
			return null;
		}

		(double Latitude, double Longitude) centre =
			zone.Centre is { } at
				? VvoCoordinateConverter.FromGk4(at.X, at.Y)
				: Centroid(zone._ring);

		return new TariffZoneShape(
			number,
			zone.Zone.Name,
			zone.Zone.Color,
			centre.Latitude,
			centre.Longitude,
			(IReadOnlyList<(double Latitude, double Longitude)>)[.. zone._ring
				.Select(point => VvoCoordinateConverter.FromGk4(point.X, point.Y))]);
	}

	private static (double Latitude, double Longitude) Centroid((double X, double Y)[] ring)
	{
		double x = 0, y = 0;

		foreach ((double X, double Y) point in ring)
		{
			x += point.X;
			y += point.Y;
		}

		return VvoCoordinateConverter.FromGk4(x / ring.Length, y / ring.Length);
	}

	/// <summary>A tariff zone with its outline in GK4 (easting, northing).</summary>
	private sealed class ZonePolygon
	{
		private readonly (double X, double Y)[] _ring;

		private ZonePolygon(TariffZone zone, (double X, double Y)[] ring, (double X, double Y)? centre)
		{
			Zone = zone;
			_ring = ring;
			Centre = centre;
			Area = Math.Abs(SignedArea(ring));
		}

		public TariffZone Zone { get; }

		public double Area { get; }

		/// <summary>"zone|name|#colour|centreN|centreE|n|e|n|e|..."; values with decimals are WGS84 degrees.</summary>
		public static ZonePolygon? Parse(string raw)
		{
			string[] fields = raw.Split('|');

			if (fields.Length < 11
				|| string.IsNullOrWhiteSpace(fields[0]))
			{
				return null;
			}

			double[] numbers =
				[.. fields
					.Skip(3)
					.Select(
						field => double.TryParse(
							field,
							NumberStyles.Float,
							CultureInfo.InvariantCulture,
							out double value)
							? value
							: double.NaN)];

			// The first pair is the centre of the zone; the rest is the outline.
			var ring = new List<(double X, double Y)>();

			for (int i = 2; i + 1 < numbers.Length; i += 2)
			{
				double first = numbers[i];
				double second = numbers[i + 1];

				if (double.IsNaN(first) || double.IsNaN(second))
				{
					continue;
				}

				if (first < 1000)
				{
					if (!VvoCoordinateConverter.TryToGk4(first, second, out (double Easting, double Northing) converted))
					{
						continue;
					}

					ring.Add((converted.Easting, converted.Northing));
				}
				else
				{
					ring.Add((second, first));
				}
			}

			// The centre (the first pair) goes the same way as a ring point, so the label lands inside the zone.
			(double X, double Y)? centre = null;

			if (numbers.Length >= 2
				&& !double.IsNaN(numbers[0])
				&& !double.IsNaN(numbers[1]))
			{
				if (numbers[0] < 1000)
				{
					if (VvoCoordinateConverter.TryToGk4(numbers[0], numbers[1], out (double Easting, double Northing) at))
					{
						centre = (at.Easting, at.Northing);
					}
				}
				else
				{
					centre = (numbers[1], numbers[0]);
				}
			}

			if (ring.Count < 3)
			{
				return null;
			}

			return new ZonePolygon(
				new TariffZone
				{
					Number = fields[0].Trim(),
					Name = fields[1].Trim(),
					Color = string.IsNullOrWhiteSpace(fields[2]) ? null : fields[2].Trim()
				},
				[.. ring],
				centre);
		}

		/// <summary>Ray casting.</summary>
		public bool Contains(double x, double y)
		{
			bool inside = false;

			for (int i = 0, j = _ring.Length - 1; i < _ring.Length; j = i++)
			{
				(double xi, double yi) = _ring[i];
				(double xj, double yj) = _ring[j];

				if ((yi > y) != (yj > y)
					&& x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
				{
					inside = !inside;
				}
			}

			return inside;
		}

		private static double SignedArea((double X, double Y)[] ring)
		{
			double sum = 0;

			for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
			{
				sum += (ring[j].X * ring[i].Y) - (ring[i].X * ring[j].Y);
			}

			return sum / 2;
		}
	}
}
