using System.Text.Json.Serialization;
using DDjourneys.Core.Api;

namespace DDjourneys.Core.Providers.Shared;

/// <summary>
/// The GBFS 2.x shapes the app reads (MOBIbike Dresden 2.3 and Lime Dresden 2.2, checked against the live feeds,
/// October 2026). Every field optional; feeds an operator does not carry are simply absent. Yes/no fields read
/// booleans and integers alike (2.3 writes true/false, older feeds 1/0: typed as int, the 2.3 status feed failed
/// whole and every station showed 0 bikes). The language buckets of a discovery file (data.de.feeds,
/// data.en.feeds) are a dictionary: the first bucket with feeds wins.
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
	[JsonConverter(typeof(LenientStringConverter))]
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

	/// <summary>A station without docks: an area to leave the bike in (MOBIbike: most of them).</summary>
	[JsonPropertyName("is_virtual_station")]
	[JsonConverter(typeof(LenientFlagConverter))]
	public bool? IsVirtualStation { get; init; }

	/// <summary>Docks of a physical station.</summary>
	[JsonPropertyName("capacity")]
	public int? Capacity { get; init; }

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

/// <summary>One station as station_status describes it. The flags come as booleans or integers, depending on the version.</summary>
public sealed class GbfsStationStatusRow
{
	[JsonPropertyName("station_id")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? StationId { get; init; }

	[JsonPropertyName("num_bikes_available")]
	public int? NumBikesAvailable { get; init; }

	[JsonPropertyName("vehicle_types_available")]
	public IReadOnlyList<GbfsVehicleType> VehicleTypesAvailable { get; init; } = [];

	[JsonPropertyName("num_docks_available")]
	public int? NumDocksAvailable { get; init; }

	[JsonPropertyName("is_installed")]
	[JsonConverter(typeof(LenientFlagConverter))]
	public bool? IsInstalled { get; init; }

	[JsonPropertyName("is_renting")]
	[JsonConverter(typeof(LenientFlagConverter))]
	public bool? IsRenting { get; init; }

	[JsonPropertyName("is_returning")]
	[JsonConverter(typeof(LenientFlagConverter))]
	public bool? IsReturning { get; init; }

	[JsonPropertyName("last_reported")]
	public long? LastReported { get; init; }
}

/// <summary>How many vehicles of one type a station has (joined with vehicle_types: e-bikes are told apart).</summary>
public sealed class GbfsVehicleType
{
	[JsonPropertyName("vehicle_type_id")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? VehicleTypeId { get; init; }

	[JsonPropertyName("count")]
	public int? Count { get; init; }
}

/// <summary>The free_bike_status feed: vehicles not at a station (and, at MOBIbike, also those at one: <c>station_id</c> set).</summary>
public sealed class GbfsFreeBikeStatus
{
	[JsonPropertyName("last_updated")]
	public long? LastUpdated { get; init; }

	[JsonPropertyName("ttl")]
	public int? Ttl { get; init; }

	[JsonPropertyName("data")]
	public GbfsBikeData? Data { get; init; }
}

public sealed class GbfsBikeData
{
	[JsonPropertyName("bikes")]
	public IReadOnlyList<GbfsBike> Bikes { get; init; } = [];
}

/// <summary>One vehicle of free_bike_status.</summary>
public sealed class GbfsBike
{
	[JsonPropertyName("bike_id")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? BikeId { get; init; }

	[JsonPropertyName("lat")]
	public double? Latitude { get; init; }

	[JsonPropertyName("lon")]
	public double? Longitude { get; init; }

	[JsonPropertyName("is_reserved")]
	[JsonConverter(typeof(LenientFlagConverter))]
	public bool? IsReserved { get; init; }

	[JsonPropertyName("is_disabled")]
	[JsonConverter(typeof(LenientFlagConverter))]
	public bool? IsDisabled { get; init; }

	[JsonPropertyName("vehicle_type_id")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? VehicleTypeId { get; init; }

	/// <summary>Lime's own word for the type ("e-bike"), beside the GBFS type id.</summary>
	[JsonPropertyName("vehicle_type")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? VehicleType { get; init; }

	/// <summary>Set when the vehicle stands at a station (counted there already).</summary>
	[JsonPropertyName("station_id")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? StationId { get; init; }

	[JsonPropertyName("current_range_meters")]
	[JsonConverter(typeof(LenientNumberConverter))]
	public double? RangeMeters { get; init; }

	[JsonPropertyName("last_reported")]
	public long? LastReported { get; init; }

	[JsonPropertyName("rental_uris")]
	public GbfsRentalUris? RentalUris { get; init; }
}

/// <summary>The vehicle_types feed: what a type id means.</summary>
public sealed class GbfsVehicleTypes
{
	[JsonPropertyName("ttl")]
	public int? Ttl { get; init; }

	[JsonPropertyName("data")]
	public GbfsVehicleTypeData? Data { get; init; }
}

public sealed class GbfsVehicleTypeData
{
	[JsonPropertyName("vehicle_types")]
	public IReadOnlyList<GbfsVehicleTypeInfo> VehicleTypes { get; init; } = [];
}

/// <summary>One vehicle type: form factor (bicycle, cargo_bicycle, scooter, ...) and propulsion (human, electric_assist, electric, ...).</summary>
public sealed class GbfsVehicleTypeInfo
{
	[JsonPropertyName("vehicle_type_id")]
	[JsonConverter(typeof(LenientStringConverter))]
	public string? VehicleTypeId { get; init; }

	[JsonPropertyName("form_factor")]
	public string? FormFactor { get; init; }

	[JsonPropertyName("propulsion_type")]
	public string? PropulsionType { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }
}
