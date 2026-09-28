using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Requests;

/// <summary>
/// Represents a VVO trip planning request.
/// </summary>
public sealed class VvoTripRequest
{
	/// <summary>
	/// Origin stop or location ID.
	/// </summary>
	[JsonPropertyName("origin")]
	public required string Origin { get; init; }


	/// <summary>
	/// Destination stop or location ID.
	/// </summary>
	[JsonPropertyName("destination")]
	public required string Destination { get; init; }


	/// <summary>
	/// Requested date and time.
	/// </summary>
	[JsonPropertyName("time")]
	public DateTimeOffset Time { get; init; }


	/// <summary>
	/// 
	/// false = depart after this time
	/// true  = arrive before this time
	/// </summary>
	[JsonPropertyName("isarrivaltime")]
	public bool IsArrivalTime { get; init; }


	/// <summary>
	/// VVO standard journey settings.
	///
	/// Kept open for future expansion.
	/// </summary>
	[JsonPropertyName("standardSettings")]
	public VvoStandardSettings StandardSettings { get; init; }
		= new();
}


/// <summary>
/// General VVO journey preferences.
/// </summary>
public sealed class VvoStandardSettings
{
	/*
     * Intentionally empty for now.
     *
     * Future examples:
     *
     * - maxChanges
     * - mobility restrictions
     * - transport mode filters
     */
}