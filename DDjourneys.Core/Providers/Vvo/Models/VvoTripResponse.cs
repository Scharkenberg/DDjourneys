using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Models;

/// <summary>
/// Represents a VVO trip planning response.
/// </summary>
public sealed class VvoTripResponse
{
	[JsonPropertyName("Status")]
	public VvoStatus? Status { get; init; }


	[JsonPropertyName("Trips")]
	public IReadOnlyList<VvoTrip> Trips { get; init; }
		= Array.Empty<VvoTrip>();
}


/// <summary>
/// Represents one VVO journey.
/// </summary>
public sealed class VvoTrip
{
	[JsonPropertyName("PartialRoutes")]
	public IReadOnlyList<VvoPartialRoute> PartialRoutes { get; init; }
		= Array.Empty<VvoPartialRoute>();


	[JsonPropertyName("Duration")]
	public int? Duration { get; init; }


	[JsonPropertyName("Changes")]
	public int? Changes { get; init; }
}


/// <summary>
/// Represents one section of a VVO journey.
/// </summary>
public sealed class VvoPartialRoute
{
	[JsonPropertyName("Mot")]
	public VvoMot? Mot { get; init; }


	[JsonPropertyName("Line")]
	public string? Line { get; init; }


	[JsonPropertyName("Direction")]
	public string? Direction { get; init; }


	[JsonPropertyName("Platform")]
	public string? Platform { get; init; }


	[JsonPropertyName("Stops")]
	public IReadOnlyList<VvoStop> Stops { get; init; }
		= Array.Empty<VvoStop>();
}


/// <summary>
/// Represents a vehicle mode returned by VVO.
/// </summary>
public sealed class VvoMot
{
	[JsonPropertyName("Name")]
	public string? Name { get; init; }


	[JsonPropertyName("Type")]
	public string? Type { get; init; }
}


/// <summary>
/// Represents a stop within a VVO route.
/// </summary>
public sealed class VvoStop
{
	[JsonPropertyName("Name")]
	public string? Name { get; init; }


	[JsonPropertyName("Place")]
	public string? Place { get; init; }


	[JsonPropertyName("Id")]
	public string? Id { get; init; }


	[JsonPropertyName("DepartureTime")]
	public DateTimeOffset? DepartureTime { get; init; }


	[JsonPropertyName("ArrivalTime")]
	public DateTimeOffset? ArrivalTime { get; init; }


	[JsonPropertyName("RealTimeDeparture")]
	public DateTimeOffset? RealTimeDeparture { get; init; }


	[JsonPropertyName("RealTimeArrival")]
	public DateTimeOffset? RealTimeArrival { get; init; }


	[JsonPropertyName("Platform")]
	public string? Platform { get; init; }
}