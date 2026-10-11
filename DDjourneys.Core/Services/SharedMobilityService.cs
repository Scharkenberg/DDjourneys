using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Shared;

namespace DDjourneys.Core.Services;

/// <summary>
/// Shared bikes and scooters for the map: stations (MOBIbike's MOBIpunkte and virtual stations) and the vehicles
/// standing free (Lime's e-bikes, MOBIbikes left in the street). Every feed is cached on its own: discovery,
/// station_information and vehicle_types for an hour, the live feeds (station_status, free_bike_status) at most a
/// minute - the feed's own ttl when it asks for less. A failed answer is never kept, so the next look asks again;
/// a dead live feed does not evict the hour-old skeleton. Answers stay the same objects while their feeds do (the
/// map tells an unchanged answer by reference).
/// </summary>
/// <remarks>
/// Operators with a public feed in Dresden (MobilityData systems list and the operators' own discovery, checked
/// October 2026): MOBIbike (Nextbike, stations and free bikes) and Lime (free e-bikes; its station_information
/// holds a single placeholder station "Dresden", so stations are not read). Voi runs e-bikes and e-scooters in
/// Dresden but publishes no open feed for it (its German GBFS on MobiData BW covers Baden-Württemberg only; its own
/// feeds need a token); Bolt and RideMovi likewise. An operator joins the list here once a feed exists.
/// </remarks>
public sealed class SharedMobilityService
{
	private static readonly TimeSpan LiveMax = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StaticLife = TimeSpan.FromHours(1);

	/// <summary>One operator: where its discovery is, what the passenger gets from it, and which app rents it.</summary>
	private sealed record Operator(
		string Id,
		string Name,
		Uri Discovery,
		Uri Website,
		string AppPackage,
		bool Stations,
		bool FreeVehicles);

	private static readonly Operator[] Operators =
	[
		new("nextbike", "MOBIbike", new Uri(InterfaceSchemas.GbfsUrl), new Uri("https://www.nextbike.de/de/dresden/"), "de.nextbike", Stations: true, FreeVehicles: true),
		new("lime", "Lime", new Uri(InterfaceSchemas.LimeGbfsUrl), new Uri("https://www.li.me/"), "com.limebike", Stations: false, FreeVehicles: true)
	];

	private readonly GbfsClient _client;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Dictionary<string, (DateTimeOffset Until, object Value)> _feeds = new(StringComparer.Ordinal);

	private readonly Dictionary<string, (object? A, object? B, object? C, IReadOnlyList<SharedStation> Value)> _stations = new(StringComparer.Ordinal);
	private readonly Dictionary<string, (object? A, object? B, IReadOnlyList<SharedVehicle> Value)> _vehicles = new(StringComparer.Ordinal);

	private IReadOnlyList<IReadOnlyList<SharedStation>> _stationParts = [];
	private IReadOnlyList<SharedStation> _stationsMerged = [];
	private IReadOnlyList<IReadOnlyList<SharedVehicle>> _vehicleParts = [];
	private IReadOnlyList<SharedVehicle> _vehiclesMerged = [];

	public SharedMobilityService(GbfsClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		_client = client;
	}

	/// <summary>The stations of every operator that has them.</summary>
	public async Task<IReadOnlyList<SharedStation>> GetStationsAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			List<IReadOnlyList<SharedStation>> parts = [];

			foreach (Operator op in Operators.Where(op => op.Stations))
			{
				parts.Add(await StationsOfAsync(op, timeout, cancellationToken).ConfigureAwait(false));
			}

			return Merge(parts, ref _stationParts, ref _stationsMerged);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>The vehicles standing free, of every operator that publishes them.</summary>
	public async Task<IReadOnlyList<SharedVehicle>> GetVehiclesAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			List<IReadOnlyList<SharedVehicle>> parts = [];

			foreach (Operator op in Operators.Where(op => op.FreeVehicles))
			{
				parts.Add(await VehiclesOfAsync(op, timeout, cancellationToken).ConfigureAwait(false));
			}

			return Merge(parts, ref _vehicleParts, ref _vehiclesMerged);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static IReadOnlyList<T> Merge<T>(
		List<IReadOnlyList<T>> parts,
		ref IReadOnlyList<IReadOnlyList<T>> previousParts,
		ref IReadOnlyList<T> previous)
	{
		if (parts.Count == previousParts.Count
			&& parts.Zip(previousParts).All(pair => ReferenceEquals(pair.First, pair.Second)))
		{
			return previous;
		}

		previousParts = parts;
		previous = [.. parts.SelectMany(part => part)];

		return previous;
	}

	private async Task<IReadOnlyList<SharedStation>> StationsOfAsync(Operator op, TimeSpan? timeout, CancellationToken cancellationToken)
	{
		GbfsDiscovery? discovery = await DiscoveryAsync(op, timeout, cancellationToken).ConfigureAwait(false);

		if (GbfsClient.FeedUrl(discovery, "station_information") is not { } informationUrl)
		{
			return [];
		}

		GbfsStationInformation? information =
			await FeedAsync(op, "station_information", informationUrl, StaticLife, _client.GetInformationAsync, item => item.Ttl, timeout, cancellationToken).ConfigureAwait(false);

		if (information is null)
		{
			// The skeleton is down and nothing of it is cached: the operator's stations vanish for this cycle.
			DiagnosticLog.Write($"[Shared] {op.Name}: no station_information; no stations this cycle");

			return [];
		}

		GbfsStationStatus? status =
			GbfsClient.FeedUrl(discovery, "station_status") is { } statusUrl
				? await FeedAsync(op, "station_status", statusUrl, LiveMax, _client.GetStatusAsync, item => item.Ttl, timeout, cancellationToken).ConfigureAwait(false)
				: null;

		GbfsVehicleTypes? types = await TypesAsync(op, discovery, timeout, cancellationToken).ConfigureAwait(false);

		if (_stations.TryGetValue(op.Id, out var previous)
			&& ReferenceEquals(previous.A, information)
			&& ReferenceEquals(previous.B, status)
			&& ReferenceEquals(previous.C, types))
		{
			return previous.Value;
		}

		IReadOnlyList<SharedStation> stations = GbfsClient.Join(information, status, op.Name, op.Website, types, op.Id, op.AppPackage);

		_stations[op.Id] = (information, status, types, stations);

		return stations;
	}

	private async Task<IReadOnlyList<SharedVehicle>> VehiclesOfAsync(Operator op, TimeSpan? timeout, CancellationToken cancellationToken)
	{
		GbfsDiscovery? discovery = await DiscoveryAsync(op, timeout, cancellationToken).ConfigureAwait(false);

		if (GbfsClient.FeedUrl(discovery, "free_bike_status") is not { } freeUrl)
		{
			return [];
		}

		GbfsFreeBikeStatus? free =
			await FeedAsync(op, "free_bike_status", freeUrl, LiveMax, _client.GetFreeBikesAsync, item => item.Ttl, timeout, cancellationToken).ConfigureAwait(false);

		if (free is null)
		{
			return _vehicles.TryGetValue(op.Id, out var stale) ? stale.Value : [];
		}

		GbfsVehicleTypes? types = await TypesAsync(op, discovery, timeout, cancellationToken).ConfigureAwait(false);

		if (_vehicles.TryGetValue(op.Id, out var previous)
			&& ReferenceEquals(previous.A, free)
			&& ReferenceEquals(previous.B, types))
		{
			return previous.Value;
		}

		IReadOnlyList<SharedVehicle> vehicles = GbfsClient.Vehicles(free, types, op.Id, op.Name, op.Website, op.AppPackage);

		_vehicles[op.Id] = (free, types, vehicles);

		return vehicles;
	}

	private Task<GbfsDiscovery?> DiscoveryAsync(Operator op, TimeSpan? timeout, CancellationToken cancellationToken) =>
		FeedAsync(op, "discovery", op.Discovery, StaticLife, _client.GetDiscoveryAsync, _ => null, timeout, cancellationToken);

	private async Task<GbfsVehicleTypes?> TypesAsync(Operator op, GbfsDiscovery? discovery, TimeSpan? timeout, CancellationToken cancellationToken) =>
		GbfsClient.FeedUrl(discovery, "vehicle_types") is { } typesUrl
			? await FeedAsync(op, "vehicle_types", typesUrl, StaticLife, _client.GetVehicleTypesAsync, _ => null, timeout, cancellationToken).ConfigureAwait(false)
			: null;

	/// <summary>
	/// One feed through its cache: kept for <paramref name="life"/>, shorter when the feed's ttl asks for it (a ttl
	/// of 0 means "changes all the time": a few seconds then, so a burst of calls still shares one answer).
	/// </summary>
	private async Task<T?> FeedAsync<T>(
		Operator op,
		string feed,
		Uri url,
		TimeSpan life,
		Func<Uri, TimeSpan?, CancellationToken, Task<T?>> fetch,
		Func<T, int?> ttl,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
		where T : class
	{
		string key = $"{op.Id}|{feed}";

		if (_feeds.TryGetValue(key, out var cached)
			&& cached.Until > DateTimeOffset.UtcNow
			&& cached.Value is T value)
		{
			return value;
		}

		T? fresh = await fetch(url, timeout, cancellationToken).ConfigureAwait(false);

		if (fresh is not null)
		{
			TimeSpan keep = ttl(fresh) is { } seconds && seconds < life.TotalSeconds
				? TimeSpan.FromSeconds(Math.Max(5, seconds))
				: life;

			_feeds[key] = (DateTimeOffset.UtcNow + keep, fresh);
		}

		return fresh;
	}
}
