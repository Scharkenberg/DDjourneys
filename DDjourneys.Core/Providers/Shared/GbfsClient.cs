using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Serialization;

namespace DDjourneys.Core.Providers.Shared;

/// <summary>
/// Reads GBFS 2.x feeds (discovery first, then the feeds it names). One feed failing never fails the operator:
/// what loaded is what the join sees. The service above this client owns the per-feed caches, so the client stays
/// stateless; every fetch degrades to null on a bad answer, and the reason is logged (a feed that silently failed to
/// parse is how every MOBIbike station came to show 0 bikes).
/// </summary>
public sealed class GbfsClient(ApiClient apiClient)
{
	public Task<GbfsDiscovery?> GetDiscoveryAsync(Uri url, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
		GetAsync(url, VvoJsonContext.Default.GbfsDiscovery, "discovery", timeout, cancellationToken);

	public Task<GbfsStationInformation?> GetInformationAsync(Uri url, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
		GetAsync(url, VvoJsonContext.Default.GbfsStationInformation, "station_information", timeout, cancellationToken);

	public Task<GbfsStationStatus?> GetStatusAsync(Uri url, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
		GetAsync(url, VvoJsonContext.Default.GbfsStationStatus, "station_status", timeout, cancellationToken);

	public Task<GbfsFreeBikeStatus?> GetFreeBikesAsync(Uri url, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
		GetAsync(url, VvoJsonContext.Default.GbfsFreeBikeStatus, "free_bike_status", timeout, cancellationToken);

	public Task<GbfsVehicleTypes?> GetVehicleTypesAsync(Uri url, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
		GetAsync(url, VvoJsonContext.Default.GbfsVehicleTypes, "vehicle_types", timeout, cancellationToken);

	private async Task<T?> GetAsync<T>(
		Uri url,
		JsonTypeInfo<T> typeInfo,
		string feed,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
		where T : class
	{
		try
		{
			string json = await apiClient.GetAsync(url.ToString(), timeout, cancellationToken).ConfigureAwait(false);

			return JsonSerializer.Deserialize(json, typeInfo);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Shared] gbfs {feed} {url.Host}{url.AbsolutePath} failed: {ex.GetType().Name}: {ex.Message}");

			return null;
		}
	}

	/// <summary>The absolute URL of one feed, whatever language bucket carries it; null when the discovery names none.</summary>
	internal static Uri? FeedUrl(GbfsDiscovery? discovery, string name)
	{
		if (discovery?.Data is not { Count: > 0 } buckets)
		{
			return null;
		}

		foreach (GbfsLanguage bucket in buckets.Values)
		{
			foreach (GbfsFeed feed in bucket.Feeds)
			{
				if (string.Equals(feed.Name, name, StringComparison.OrdinalIgnoreCase)
					&& Uri.TryCreate(feed.Url, UriKind.Absolute, out Uri? url)
					&& url is { Scheme: "http" or "https" })
				{
					return url;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// The stations of one operator: the skeleton from station_information, the counts from station_status (a missing
	/// or failed status leaves the stations standing, grey, without numbers that could lie). Physical stations (with
	/// docks) and virtual ones (an area) are told apart; the e-bikes among the bikes are counted where the operator
	/// says which types are electric.
	/// </summary>
	internal static IReadOnlyList<SharedStation> Join(
		GbfsStationInformation? information,
		GbfsStationStatus? status,
		string operatorName,
		Uri? website = null,
		GbfsVehicleTypes? types = null,
		string? operatorId = null,
		string? appPackage = null)
	{
		if (information?.Data?.Stations is not { Count: > 0 } skeleton)
		{
			return [];
		}

		Dictionary<string, GbfsStationStatusRow>? rows = null;

		if (status?.Data?.Stations is { Count: > 0 } reported)
		{
			rows = new Dictionary<string, GbfsStationStatusRow>(StringComparer.Ordinal);

			foreach (GbfsStationStatusRow row in reported)
			{
				if (row.StationId is { Length: > 0 } id)
				{
					rows[id] = row;
				}
			}
		}

		Dictionary<string, SharedKind> kinds = Kinds(types);
		List<SharedStation> joined = new(skeleton.Count);

		foreach (GbfsStation station in skeleton)
		{
			if (station.StationId is not { Length: > 0 } stationId
				|| station.Latitude is not { } latitude
				|| station.Longitude is not { } longitude)
			{
				continue;
			}

			GbfsStationStatusRow? row = rows?.TryGetValue(stationId, out GbfsStationStatusRow? found) == true ? found : null;

			// A station that is not installed is not there.
			if (row?.IsInstalled is false)
			{
				continue;
			}

			int electric = CountOf(row, kinds, SharedKind.EBike, SharedKind.Scooter);
			int cargo = CountOf(row, kinds, SharedKind.CargoBike);

			bool isVirtual = station.IsVirtualStation ?? false;

			joined.Add(
				new SharedStation(
					operatorName,
					stationId,
					station.Name ?? station.ShortName ?? stationId,
					latitude,
					longitude,
					Math.Max(0, row?.NumBikesAvailable ?? 0),
					isVirtual ? null : row?.NumDocksAvailable,
					row?.IsRenting,
					AbsoluteLink(station.RentalUris?.Android),
					AbsoluteWeb(station.RentalUris?.Web),
					website,
					row?.LastReported is { } reportedAt ? FromPosix(reportedAt) : null,
					operatorId ?? operatorName,
					isVirtual,
					isVirtual ? null : station.Capacity,
					electric,
					appPackage,
					cargo));
		}

		return joined;
	}

	/// <summary>
	/// The vehicles standing free: free_bike_status without the reserved, the disabled and those at a station (a
	/// station counts its own). Their kind comes from vehicle_types, else from the operator's own word for it.
	/// </summary>
	internal static IReadOnlyList<SharedVehicle> Vehicles(
		GbfsFreeBikeStatus? free,
		GbfsVehicleTypes? types,
		string operatorId,
		string operatorName,
		Uri? website = null,
		string? appPackage = null)
	{
		if (free?.Data?.Bikes is not { Count: > 0 } bikes)
		{
			return [];
		}

		Dictionary<string, SharedKind> kinds = Kinds(types);
		List<SharedVehicle> vehicles = new(bikes.Count);

		foreach (GbfsBike bike in bikes)
		{
			if (bike.BikeId is not { Length: > 0 } id
				|| bike.Latitude is not { } latitude
				|| bike.Longitude is not { } longitude
				|| bike.IsReserved is true
				|| bike.IsDisabled is true
				|| !string.IsNullOrEmpty(bike.StationId))
			{
				continue;
			}

			SharedKind kind =
				bike.VehicleTypeId is { } type && kinds.TryGetValue(type, out SharedKind known)
					? known
					: KindOfWord(bike.VehicleType);

			vehicles.Add(
				new SharedVehicle(
					operatorId,
					operatorName,
					id,
					latitude,
					longitude,
					kind,
					bike.RangeMeters is { } range and > 0 ? (int)Math.Round(range) : null,
					AbsoluteLink(bike.RentalUris?.Android),
					AbsoluteWeb(bike.RentalUris?.Web),
					website,
					appPackage,
					bike.LastReported is { } reportedAt ? FromPosix(reportedAt) : null));
		}

		return vehicles;
	}

	/// <summary>How many of a station's vehicles are of the kinds asked for.</summary>
	private static int CountOf(GbfsStationStatusRow? row, Dictionary<string, SharedKind> kinds, params SharedKind[] wanted) =>
		row?.VehicleTypesAvailable
			.Where(available => available.VehicleTypeId is { } type
				&& kinds.TryGetValue(type, out SharedKind kind)
				&& wanted.Contains(kind))
			.Sum(available => Math.Max(0, available.Count ?? 0))
		?? 0;

	/// <summary>Type id to kind, from form factor and propulsion.</summary>
	internal static Dictionary<string, SharedKind> Kinds(GbfsVehicleTypes? types)
	{
		var kinds = new Dictionary<string, SharedKind>(StringComparer.Ordinal);

		foreach (GbfsVehicleTypeInfo type in types?.Data?.VehicleTypes ?? [])
		{
			if (type.VehicleTypeId is { Length: > 0 } id)
			{
				kinds[id] = KindOf(type.FormFactor, type.PropulsionType);
			}
		}

		return kinds;
	}

	internal static SharedKind KindOf(string? formFactor, string? propulsion)
	{
		string form = formFactor?.ToLowerInvariant() ?? string.Empty;
		bool electric = propulsion is not null && !propulsion.Equals("human", StringComparison.OrdinalIgnoreCase);

		return form switch
		{
			_ when form.StartsWith("scooter", StringComparison.Ordinal) || form == "moped" => SharedKind.Scooter,
			"cargo_bicycle" => SharedKind.CargoBike,
			_ => electric ? SharedKind.EBike : SharedKind.Bike
		};
	}

	/// <summary>Lime's own vehicle word ("e-bike", "scooter") when no type list says.</summary>
	internal static SharedKind KindOfWord(string? word) =>
		word?.ToLowerInvariant() switch
		{
			null or "" => SharedKind.Bike,
			var text when text.Contains("scooter", StringComparison.Ordinal) => SharedKind.Scooter,
			var text when text.Contains("cargo", StringComparison.Ordinal) => SharedKind.CargoBike,
			var text when text.Contains("e-bike", StringComparison.Ordinal) || text.Contains("ebike", StringComparison.Ordinal) => SharedKind.EBike,
			_ => SharedKind.Bike
		};

	/// <summary>GBFS 2.x counts POSIX seconds, newer feeds milliseconds; both are told apart by their size.</summary>
	internal static DateTimeOffset FromPosix(long value) =>
		DateTimeOffset.FromUnixTimeMilliseconds(
			value > 100_000_000_000L ? value : checked(value * 1000));

	/// <summary>An app link: any absolute scheme goes (https App Links and custom schemes alike).</summary>
	private static Uri? AbsoluteLink(string? text) =>
		Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme.Length > 0
			? uri
			: null;

	/// <summary>A web page: only http and https count.</summary>
	private static Uri? AbsoluteWeb(string? text) =>
		Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri is { Scheme: "http" or "https" }
			? uri
			: null;
}
