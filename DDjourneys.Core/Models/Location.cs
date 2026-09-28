namespace DDjourneys.Core.Models;

/// <summary>
/// Represents a geographic or public transport location
/// used as the start or destination of a journey search.
/// </summary>
public sealed class Location
{
	/// <summary>
	/// Provider-specific identifier.
	///
	/// Usually available for stations.
	/// </summary>
	public string? Id { get; init; }


	/// <summary>
	/// Display name of the location.
	///
	/// Examples:
	/// "Dresden Hbf"
	/// "Semperoper"
	/// "Hauptstraße 12"
	/// </summary>
	public required string Name { get; init; }


	/// <summary>
	/// Optional locality or city.
	/// </summary>
	public string? Place { get; init; }


	/// <summary>
	/// Latitude in WGS84 coordinates.
	/// </summary>
	public double? Latitude { get; init; }


	/// <summary>
	/// Longitude in WGS84 coordinates.
	/// </summary>
	public double? Longitude { get; init; }


	/// <summary>
	/// Indicates whether this location represents a public transport stop.
	/// </summary>
	public bool IsStation =>
		!string.IsNullOrWhiteSpace(Id);


	public override string ToString()
	{
		return string.IsNullOrWhiteSpace(Place)
			? Name
			: $"{Name}, {Place}";
	}
}