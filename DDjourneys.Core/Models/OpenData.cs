namespace DDjourneys.Core.Models;

/// <summary>Accessibility of one platform of a stop (Dresden open data).</summary>
public sealed class StopAccessibility
{
	public required string StopName { get; init; }

	public string? Platform { get; init; }

	/// <summary>Global stop id ("de:14612:28").</summary>
	public string? GlobalId { get; init; }

	/// <summary>How best to board (level, ramp, ...), as the city words it.</summary>
	public string? Boarding { get; init; }

	public string? KerbHeight { get; init; }

	public string? Width { get; init; }

	/// <summary>Tactile guidance system for blind passengers.</summary>
	public string? TactileGuidance { get; init; }

	/// <summary>Audio announcements.</summary>
	public string? AudioAnnouncements { get; init; }

	public double? Latitude { get; init; }

	public double? Longitude { get; init; }
}


/// <summary>A DVB service point (customer centre, ticket sales).</summary>
public sealed class ServicePoint
{
	public required string Name { get; init; }

	/// <summary>Every other attribute the city publishes, as label and text.</summary>
	public IReadOnlyList<KeyValuePair<string, string>> Details { get; init; } = [];

	public double Latitude { get; init; }

	public double Longitude { get; init; }

	public int DistanceMeters { get; init; }
}
