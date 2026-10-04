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
	/// false = depart after this time.
	/// true = arrive before this time.
	/// </summary>
	[JsonPropertyName("isarrivaltime")]
	public bool IsArrivalTime { get; init; }


	/// <summary>
	/// Include current short-term route changes and disruption data.
	/// </summary>
	[JsonPropertyName("shorttermchanges")]
	public bool ShortTermChanges { get; init; } = true;


	/// <summary>
	/// Intermediate stop the journey has to pass through (stop id); omitted when there is none.
	/// </summary>
	[JsonPropertyName("via")]
	public string? Via { get; init; }


	/// <summary>
	/// General routing preferences.
	/// </summary>
	[JsonPropertyName("standardSettings")]
	public VvoStandardSettings StandardSettings { get; init; }
		= new();


	/// <summary>
	/// Accessibility-related routing preferences.
	/// </summary>
	[JsonPropertyName("mobilitySettings")]
	public VvoMobilitySettings MobilitySettings { get; init; }
		= new();
}


/// <summary>
/// General VVO journey preferences.
/// </summary>
public sealed class VvoStandardSettings
{
	/// <summary>
	/// Maximum number of transfers.
	/// VVO values: Unlimited, Two, One, None.
	/// </summary>
	[JsonPropertyName("maxChanges")]
	public string MaxChanges { get; init; } = "Unlimited";


	/// <summary>
	/// Preferred walking speed.
	/// VVO values: VerySlow, Slow, Normal, Fast, VeryFast.
	/// </summary>
	[JsonPropertyName("walkingSpeed")]
	public string WalkingSpeed { get; init; } = "Normal";


	/// <summary>
	/// Maximum walking time to an alternative stop, in minutes.
	/// </summary>
	[JsonPropertyName("footpathToStop")]
	public int FootpathToStop { get; init; } = 5;


	/// <summary>
	/// Allow nearby alternative stops when planning.
	/// </summary>
	[JsonPropertyName("includeAlternativeStops")]
	public bool IncludeAlternativeStops { get; init; } = true;


	/// <summary>
	/// Additional fare restriction.
	/// Empty means no additional restriction.
	/// </summary>
	[JsonPropertyName("extraCharge")]
	public string ExtraCharge { get; init; } = string.Empty;


	/// <summary>
	/// Allowed VVO modes of transport.
	/// </summary>
	[JsonPropertyName("mot")]
	public IReadOnlyList<string> ModesOfTransport { get; init; } =
	[
		"Tram",
		"CityBus",
		"IntercityBus",
		"SuburbanRailway",
		"Train",
		"Cableway",
		"Ferry",
		"HailedSharedTaxi"
	];
}


/// <summary>
/// VVO accessibility-related journey preferences.
/// </summary>
public sealed class VvoMobilitySettings
{
	/// <summary>
	/// Accessibility restriction.
	/// VVO values: None, Medium, High, Individual.
	/// </summary>
	[JsonPropertyName("mobilityRestriction")]
	public string MobilityRestriction { get; init; } = "None";


	/// <summary>
	/// Allow stair movement when using Individual accessibility settings.
	/// </summary>
	[JsonPropertyName("solidStairs")]
	public bool SolidStairs { get; init; } = true;


	/// <summary>
	/// Allow escalators when using Individual accessibility settings.
	/// </summary>
	[JsonPropertyName("escalators")]
	public bool Escalators { get; init; } = true;


	/// <summary>
	/// Prefer the route with the fewest transfers when using Individual settings.
	/// </summary>
	[JsonPropertyName("leastChange")]
	public bool LeastChange { get; init; } = false;


	/// <summary>
	/// Required vehicle entrance.
	/// VVO values: Any, SmallStep, NoStep.
	/// </summary>
	[JsonPropertyName("entrance")]
	public string Entrance { get; init; } = "Any";
}