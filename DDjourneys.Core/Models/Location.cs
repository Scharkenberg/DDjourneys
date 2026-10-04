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
	/// Usually available for stations. Only meaningful together with <see cref="ProviderId"/>.
	/// </summary>
	public string? Id { get; init; }


	/// <summary>
	/// Stable id of the provider that issued <see cref="Id"/> (see <c>ProviderInfo.Id</c>).
	/// Empty for places that were not issued by a provider.
	/// </summary>
	public string ProviderId { get; init; } = string.Empty;


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
	/// What this is. Stored places from before addresses existed are stops.
	/// </summary>
	public PlaceKind Kind { get; init; } = PlaceKind.Stop;


	/// <summary>
	/// Indicates whether this location represents a public transport stop.
	/// </summary>
	public bool IsStation =>
		Kind == PlaceKind.Stop
		&& !string.IsNullOrWhiteSpace(Id);


	/// <summary>The router can start or end here (stop, address, point of interest or an exact position).</summary>
	public bool IsRoutable =>
		Kind is PlaceKind.Stop or PlaceKind.Address or PlaceKind.Poi or PlaceKind.Coordinate
		&& !string.IsNullOrWhiteSpace(Id);


	/// <summary>Provider-qualified stop key ("vvo:33000028"); null for free-form places.</summary>
	public string? StopKey =>
		ProviderKey.Compose(
			ProviderId,
			Id);


	public override string ToString()
	{
		return string.IsNullOrWhiteSpace(Place)
			? Name
			: $"{Name}, {Place}";
	}
}
