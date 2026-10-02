namespace DDjourneys.Core.Models;

/// <summary>
/// Represents a public transport stop or station.
/// </summary>
public sealed class Station
{
	/// <summary>
	/// Provider-specific identifier of the station.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>
	/// Human-readable stop name.
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// City or locality containing the station.
	/// </summary>
	public string? Place { get; init; }

	/// <summary>
	/// Geographic latitude in WGS84 coordinates.
	/// </summary>
	public double? Latitude { get; init; }

	/// <summary>
	/// Geographic longitude in WGS84 coordinates.
	/// </summary>
	public double? Longitude { get; init; }

	/// <summary>
	/// Optional platform or stop section identifier.
	/// 
	/// Example:
	/// "Gleis 3"
	/// "Platform 5"
	/// </summary>
	public string? Platform { get; init; }

	/// <summary>Whether <see cref="Platform"/> is a platform (Steig) or a track (Gleis).</summary>
	public PlatformKind PlatformKind { get; init; }

	public override string ToString()
	{
		return string.IsNullOrWhiteSpace(Place)
			? Name
			: $"{Name}, {Place}";
	}
}