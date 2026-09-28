namespace DDjourneys.Core.Providers.Vvo.Models;

/// <summary>
/// Represents one raw point returned by the VVO PointFinder API.
/// 
/// This is a provider DTO and must not leak into the application layer.
/// </summary>
public sealed class VvoPoint
{
	/// <summary>
	/// Provider identifier.
	///
	/// Usually a numeric stop ID.
	/// </summary>
	public string? Id { get; init; }


	/// <summary>
	/// Optional point type information.
	///
	/// Examples:
	/// stop
	/// POI
	/// coordinate
	/// </summary>
	public string? Type { get; init; }


	/// <summary>
	/// City or locality.
	/// </summary>
	public string? Place { get; init; }


	/// <summary>
	/// Human-readable name.
	/// </summary>
	public string? Name { get; init; }


	/// <summary>
	/// Raw first coordinate value returned by VVO.
	///
	/// Usually GK4 latitude.
	/// </summary>
	public string? Coordinate1 { get; init; }


	/// <summary>
	/// Raw second coordinate value returned by VVO.
	///
	/// Usually GK4 longitude.
	/// </summary>
	public string? Coordinate2 { get; init; }


	/// <summary>
	/// Remaining provider-specific fields.
	/// </summary>
	public IReadOnlyList<string> AdditionalData { get; init; }
		= Array.Empty<string>();


	/// <summary>
	/// Original unmodified response string.
	/// Useful for diagnostics.
	/// </summary>
	public required string Raw { get; init; }
}