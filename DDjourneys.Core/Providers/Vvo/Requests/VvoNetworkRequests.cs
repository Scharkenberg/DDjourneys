using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Requests;

/// <summary>Departure monitor request (POST dm).</summary>
public sealed class VvoDepartureRequest
{
	[JsonPropertyName("stopid")]
	public required string StopId { get; init; }

	[JsonPropertyName("limit")]
	public int? Limit { get; init; }

	/// <summary>ISO 8601 timestamp; omitted: now.</summary>
	[JsonPropertyName("time")]
	public string? Time { get; init; }

	[JsonPropertyName("isarrival")]
	public bool IsArrival { get; init; }

	[JsonPropertyName("shorttermchanges")]
	public bool ShortTermChanges { get; init; } = true;

	/// <summary>Allowed modes of transport; omitted: all.</summary>
	[JsonPropertyName("mot")]
	public IReadOnlyList<string>? ModesOfTransport { get; init; }

	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>Course of a run (POST dm/trip).</summary>
public sealed class VvoDepartureRunRequest
{
	/// <summary>Departure id from the monitor.</summary>
	[JsonPropertyName("tripid")]
	public required string TripId { get; init; }

	/// <summary>"/Date(milliseconds+0000)/".</summary>
	[JsonPropertyName("time")]
	public required string Time { get; init; }

	[JsonPropertyName("stopid")]
	public required string StopId { get; init; }

	[JsonPropertyName("isarrival")]
	public bool IsArrival { get; init; }

	[JsonPropertyName("mapdata")]
	public bool MapData { get; init; }

	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>Alternative connection for one leg (POST tr/prevnextmove). Note the lower-case ids, as documented.</summary>
public sealed class VvoPrevNextMoveRequest
{
	[JsonPropertyName("origin")]
	public required string Origin { get; init; }

	[JsonPropertyName("destination")]
	public required string Destination { get; init; }

	[JsonPropertyName("sessionid")]
	public required string SessionId { get; init; }

	[JsonPropertyName("routeid")]
	public required string RouteId { get; init; }

	[JsonPropertyName("partialrouteid")]
	public required string PartialRouteId { get; init; }

	[JsonPropertyName("time")]
	public DateTimeOffset Time { get; init; }

	[JsonPropertyName("via")]
	public string? Via { get; init; }

	[JsonPropertyName("standardSettings")]
	public VvoStandardSettings StandardSettings { get; init; } = new();

	[JsonPropertyName("mobilitySettings")]
	public VvoMobilitySettings MobilitySettings { get; init; } = new();

	/// <summary>true: earlier alternative; false: later.</summary>
	[JsonPropertyName("previous")]
	public bool Previous { get; init; }

	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>Route changes (POST rc).</summary>
public sealed class VvoRouteChangesRequest
{
	[JsonPropertyName("shortterm")]
	public bool ShortTerm { get; init; }

	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>Lines with route changes (POST rc/lines).</summary>
public sealed class VvoChangedLinesRequest
{
	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>Lines of a stop (POST stt/lines).</summary>
public sealed class VvoStopLinesRequest
{
	[JsonPropertyName("stopid")]
	public required string StopId { get; init; }

	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>Map markers in a box of GK4 coordinates (POST map/pins). The values are strings, as documented.</summary>
public sealed class VvoMapPinsRequest
{
	[JsonPropertyName("swlat")]
	public required string SouthWestLatitude { get; init; }

	[JsonPropertyName("swlng")]
	public required string SouthWestLongitude { get; init; }

	[JsonPropertyName("nelat")]
	public required string NorthEastLatitude { get; init; }

	[JsonPropertyName("nelng")]
	public required string NorthEastLongitude { get; init; }

	/// <summary>Stop, Platform, Poi, RentABike, CarSharing, TicketMachine, ParkAndRide.</summary>
	[JsonPropertyName("pintypes")]
	public IReadOnlyList<string> PinTypes { get; init; } = ["Stop"];

	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>Tariff zone polygons (POST map/polygons).</summary>
public sealed class VvoMapPolygonsRequest
{
	[JsonPropertyName("format")]
	public string Format { get; init; } = "json";
}


/// <summary>What the PointFinder is asked (GET tr/pointfinder).</summary>
public sealed record VvoPointFinderOptions
{
	public int Limit { get; init; } = 30;

	public bool StopsOnly { get; init; } = true;

	/// <summary>Only the VVO area.</summary>
	public bool RegionalOnly { get; init; }

	/// <summary>Also match stop shortcuts ("Hbf").</summary>
	public bool StopShortcuts { get; init; }

	/// <summary>Stops assigned to a coordinate (with a <c>coord:</c> query).</summary>
	public bool AssignedStops { get; init; }

	/// <summary>Include the lines of each stop.</summary>
	public bool ShowLines { get; init; }
}
