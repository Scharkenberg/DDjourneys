namespace DDjourneys.Core.Models;

/// <summary>
/// Describes a request to find journeys between two locations.
/// </summary>
public sealed class JourneyQuery
{
	/// <summary>
	/// Starting station or location.
	/// </summary>
	public required Location From { get; init; }


	/// <summary>
	/// Destination station or location.
	/// </summary>
	public required Location To { get; init; }


	/// <summary>
	/// Requested date and time.
	/// 
	/// Interpretation depends on SearchMode.
	/// </summary>
	public DateTimeOffset DateTime { get; init; }
		= DateTimeOffset.Now;


	/// <summary>
	/// Whether DateTime represents departure or arrival.
	/// </summary>
	public JourneySearchMode SearchMode { get; init; }
		= JourneySearchMode.Departure;


	/// <summary>
	/// Maximum number of journeys requested.
	/// 
	/// Used as a hint for providers that support limiting results.
	/// </summary>
	public int MaxResults { get; init; }
		= 5;

	/// <summary>Maximum duration of a provider request, in seconds.</summary>
	public int TimeoutSeconds { get; init; } = 15;

}


/// <summary>
/// Defines how the requested time is interpreted.
/// </summary>
public enum JourneySearchMode
{
	/// <summary>
	/// Find journeys leaving after the requested time.
	/// </summary>
	Departure = 0,


	/// <summary>
	/// Find journeys arriving before the requested time.
	/// </summary>
	Arrival = 1
}
