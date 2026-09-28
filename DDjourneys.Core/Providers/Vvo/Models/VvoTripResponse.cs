using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Models;

/// <summary>
/// Represents a VVO trip planning response.
/// </summary>
public sealed class VvoTripResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }


	[JsonPropertyName("SessionId")]
	public string? SessionId { get; init; }


	[JsonPropertyName("Routes")]
	public IReadOnlyList<VvoRoute> Routes { get; init; }
		= Array.Empty<VvoRoute>();
}


/// <summary>
/// Represents one complete journey option returned by VVO.
/// </summary>
public sealed class VvoRoute
{
	[JsonPropertyName("RouteId")]
	public int RouteId { get; init; }


	[JsonPropertyName("Duration")]
	public int Duration { get; init; }


	[JsonPropertyName("Interchanges")]
	public int Interchanges { get; init; }


	[JsonPropertyName("RouteCancelled")]
	public bool RouteCancelled { get; init; }


	[JsonPropertyName("MotChain")]
	public IReadOnlyList<VvoMot> MotChain { get; init; }
		= Array.Empty<VvoMot>();


	[JsonPropertyName("PartialRoutes")]
	public IReadOnlyList<VvoPartialRoute> PartialRoutes { get; init; }
		= Array.Empty<VvoPartialRoute>();
}


/// <summary>
/// Represents one leg of a journey.
/// </summary>
public sealed class VvoPartialRoute
{
	[JsonPropertyName("PartialRouteId")]
	public int PartialRouteId { get; init; }


	[JsonPropertyName("Duration")]
	public int Duration { get; init; }


	[JsonPropertyName("TripCancelled")]
	public bool TripCancelled { get; init; }


	[JsonPropertyName("ChangeoverEndangered")]
	public bool ChangeoverEndangered { get; init; }


	[JsonPropertyName("Infos")]
	public IReadOnlyList<string> Infos { get; init; }
		= Array.Empty<string>();


	[JsonPropertyName("Mot")]
	public VvoMot? Mot { get; init; }


	[JsonPropertyName("RegularStops")]
	public IReadOnlyList<VvoStop> RegularStops { get; init; }
		= Array.Empty<VvoStop>();
}


/// <summary>
/// Represents transport information for a VVO leg.
/// </summary>
public sealed class VvoMot
{
	[JsonPropertyName("Name")]
	public string? Name { get; init; }


	[JsonPropertyName("Type")]
	public string? Type { get; init; }


	[JsonPropertyName("Direction")]
	public string? Direction { get; init; }


	[JsonPropertyName("Diva")]
	public VvoDiva? Diva { get; init; }
}


/// <summary>
/// Represents VVO line identity information.
/// </summary>
public sealed class VvoDiva
{
	[JsonPropertyName("Network")]
	public string? Network { get; init; }


	[JsonPropertyName("Number")]
	public string? Number { get; init; }
}


/// <summary>
/// Represents one stop in a VVO journey leg.
/// </summary>
public sealed class VvoStop
{
	[JsonPropertyName("DataId")]
	public string? DataId { get; init; }


	[JsonPropertyName("Name")]
	public string? Name { get; init; }


	[JsonPropertyName("Place")]
	public string? Place { get; init; }


	[JsonPropertyName("Type")]
	public string? Type { get; init; }


	[JsonPropertyName("Platform")]
	public VvoPlatform? Platform { get; init; }


	[JsonPropertyName("ArrivalTime")]
	public DateTimeOffset? ArrivalTime { get; init; }


	[JsonPropertyName("DepartureTime")]
	public DateTimeOffset? DepartureTime { get; init; }


	[JsonPropertyName("ArrivalRealTime")]
	public DateTimeOffset? ArrivalRealTime { get; init; }


	[JsonPropertyName("DepartureRealTime")]
	public DateTimeOffset? DepartureRealTime { get; init; }


	[JsonPropertyName("ArrivalState")]
	public string? ArrivalState { get; init; }


	[JsonPropertyName("DepartureState")]
	public string? DepartureState { get; init; }


	[JsonPropertyName("Occupancy")]
	public string? Occupancy { get; init; }
}


/// <summary>
/// Represents a VVO platform/track object.
/// </summary>
public sealed class VvoPlatform
{
	[JsonPropertyName("Name")]
	public string? Name { get; init; }


	[JsonPropertyName("Type")]
	public string? Type { get; init; }
}