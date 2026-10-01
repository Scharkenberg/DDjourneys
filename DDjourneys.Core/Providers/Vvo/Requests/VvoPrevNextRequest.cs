using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Requests;

/// <summary>
/// Requests earlier or later journey connections from a VVO trip-planning session.
/// </summary>
public sealed class VvoPrevNextRequest
{
	[JsonPropertyName("origin")]
	public required string Origin { get; init; }


	[JsonPropertyName("destination")]
	public required string Destination { get; init; }


	[JsonPropertyName("sessionId")]
	public required string SessionId { get; init; }


	[JsonPropertyName("time")]
	public DateTimeOffset Time { get; init; }


	[JsonPropertyName("isarrivaltime")]
	public bool IsArrivalTime { get; init; }


	[JsonPropertyName("shorttermchanges")]
	public bool ShortTermChanges { get; init; } = true;


	[JsonPropertyName("standardSettings")]
	public VvoStandardSettings StandardSettings { get; init; }
		= new();


	[JsonPropertyName("mobilitySettings")]
	public VvoMobilitySettings MobilitySettings { get; init; }
		= new();


	/// <summary>
	/// true requests earlier connections;
	/// false requests later connections.
	/// </summary>
	[JsonPropertyName("previous")]
	public bool Previous { get; init; }


	/// <summary>
	/// Number of earlier connections requested.
	/// </summary>
	[JsonPropertyName("numberprev")]
	public int NumberPrevious { get; init; }


	/// <summary>
	/// Number of later connections requested.
	/// </summary>
	[JsonPropertyName("numbernext")]
	public int NumberNext { get; init; }
}