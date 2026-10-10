using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Shared;

/// <summary>
/// The GBFS 2.x shapes the app reads (MOBIbike Dresden, verified live during the research session). Every field
/// optional; feeds a station does not carry are simply absent. The language buckets of a discovery file
/// (data.de.feeds, data.en.feeds) are a dictionary: the first bucket with feeds wins.
/// </summary>
public sealed class GbfsDiscovery
{
	[JsonPropertyName("last_updated")]
	public long? LastUpdated { get; init; }

	[JsonPropertyName("ttl")]
	public int? Ttl { get; init; }

	[JsonPropertyName("data")]
	public Dictionary<string, GbfsLanguage>? Data { get; init; }
}

/// <summary>One language bucket of a discovery file.</summary>
public sealed class GbfsLanguage
{
	[JsonPropertyName("feeds")]
	public IReadOnlyList<GbfsFeed> Feeds { get; init; } = [];
}

/// <summary>One feed of a discovery file: a name and its absolute URL.</summary>
public sealed class GbfsFeed
{
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("url")]
	public string? Url { get; init; }
}

/// <summary>The station_information feed: the near-static skeleton (name, position, rental links).</summary>
public sealed class GbfsStationInformation
{
	[JsonPropertyName("last_updated")]
	public long? LastUpdated { get; init; }

	[JsonPropertyName("ttl")]
	public int? Ttl { get; init; }

	[JsonPropertyName("data")]
	public GbfsStationData? Data { get; init; }
}

public sealed class GbfsStationData
{
	[JsonPropertyName("stations")]
	public IReadOnlyList<GbfsStation> Stations { get; init; } = [];
}

/// <summary>One station as station_information describes it.</summary>
public sealed class GbfsStation
{
	[JsonPropertyName("station_id")]
	public string? StationId { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("short_name")]
	public string? ShortName { get; init; }

	[JsonPropertyName("lat")]
	public double? Latitude { get; init; }

	[JsonPropertyName("lon")]
	public double? Longitude { get; init; }

	[JsonPropertyName("region_id")]
	public string? RegionId { get; init; }

	[JsonPropertyName("is_virtual")]
	public int? IsVirtual { get; init; }

	[JsonPropertyName("rental_uris")]
	public GbfsRentalUris? RentalUris { get; init; }
}

/// <summary>Where a station can be rented from: the app deep links and the web page.</summary>
public sealed class GbfsRentalUris
{
	[JsonPropertyName("android")]
	public string? Android { get; init; }

	[JsonPropertyName("ios")]
	public string? Ios { get; init; }

	[JsonPropertyName("web")]
	public string? Web { get; init; }
}

/// <summary>The station_status feed: the minute-scale counts.</summary>
public sealed class GbfsStationStatus
{
	[JsonPropertyName("last_updated")]
	public long? LastUpdated { get; init; }

	[JsonPropertyName("ttl")]
	public int? Ttl { get; init; }

	[JsonPropertyName("data")]
	public GbfsStatusData? Data { get; init; }
}

public sealed class GbfsStatusData
{
	[JsonPropertyName("stations")]
	public IReadOnlyList<GbfsStationStatusRow> Stations { get; init; } = [];
}

/// <summary>One station as station_status describes it. GBFS 2.x carries the flags as integers (1 true, 0 false).</summary>
public sealed class GbfsStationStatusRow
{
	[JsonPropertyName("station_id")]
	public string? StationId { get; init; }

	[JsonPropertyName("num_bikes_available")]
	public int? NumBikesAvailable { get; init; }

	[JsonPropertyName("vehicle_types_available")]
	public IReadOnlyList<GbfsVehicleType> VehicleTypesAvailable { get; init; } = [];

	[JsonPropertyName("num_docks_available")]
	public int? NumDocksAvailable { get; init; }

	[JsonPropertyName("is_installed")]
	public int? IsInstalled { get; init; }

	[JsonPropertyName("is_renting")]
	public int? IsRenting { get; init; }

	[JsonPropertyName("is_returning")]
	public int? IsReturning { get; init; }

	[JsonPropertyName("last_reported")]
	public long? LastReported { get; init; }
}

/// <summary>Parsed but unused in v1: the split of bikes by vehicle type (a later extra).</summary>
public sealed class GbfsVehicleType
{
	[JsonPropertyName("vehicle_type_id")]
	public string? VehicleTypeId { get; init; }

	[JsonPropertyName("count")]
	public int? Count { get; init; }
}
