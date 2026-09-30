namespace DDjourneys.Core.Models;

/// <summary>
/// Represents the occupancy level of a vehicle or stop.
/// </summary>
public enum OccupancyLevel
{
	/// <summary>Occupancy information is not available.</summary>
	Unknown = 0,

	/// <summary>Very few passengers, plenty of space.</summary>
	VeryLow,

	/// <summary>Few passengers, good space available.</summary>
	Low,

	/// <summary>Moderate number of passengers.</summary>
	Medium,

	/// <summary>Many passengers, limited space.</summary>
	High,

	/// <summary>Vehicle is full or nearly full.</summary>
	Full,

	/// <summary>Vehicle is overloaded, standing room only or no space.</summary>
	Overloaded
}

/// <summary>
/// Represents a physical or operational transport vehicle.
///
/// Many transport providers do not expose this information.
/// In that case a JourneyLeg simply has no Vehicle assigned.
/// </summary>
public sealed class Vehicle
{
	/// <summary>
	/// Provider-specific vehicle identifier.
	///
	/// Examples:
	/// "ET442-123"
	/// "ICE-728"
	/// "BUS-4815"
	/// </summary>
	public string? Id { get; init; }

	/// <summary>
	/// Human-readable vehicle name or number.
	///
	/// Examples:
	/// "442 123"
	/// "ICE 4"
	/// </summary>
	public string? Name { get; init; }

	/// <summary>
	/// Operator owning or operating the vehicle.
	///
	/// Examples:
	/// DB Regio
	/// DVB
	/// </summary>
	public string? Operator { get; init; }

	/// <summary>
	/// Operator code identifier.
	///
	/// Examples:
	/// "8004"
	/// "LD"
	/// </summary>
	public string? OperatorCode { get; init; }

	/// <summary>
	/// Product name or category.
	///
	/// Examples:
	/// "S-Bahn"
	/// "Zug"
	/// </summary>
	public string? ProductName { get; init; }

	/// <summary>
	/// VVO data link identifier.
	/// </summary>
	public string? DlId { get; init; }

	/// <summary>
	/// Provider-independent identifier.
	/// </summary>
	public string? StatelessId { get; init; }

	/// <summary>
	/// Current occupancy level of the vehicle.
	/// </summary>
	public OccupancyLevel Occupancy { get; init; } = OccupancyLevel.Unknown;

	/// <summary>
	/// Optional vehicle accessibility information.
	/// </summary>
	public AccessibilityInfo? Accessibility { get; init; }

	/// <summary>
	/// Current geographic position, if available.
	///
	/// Usually only provided by realtime APIs.
	/// </summary>
	public GeoPosition? Position { get; init; }
}

/// <summary>
/// Accessibility properties of a vehicle.
/// </summary>
public sealed class AccessibilityInfo
{
	/// <summary>
	/// Whether wheelchair access is available.
	/// </summary>
	public bool? WheelchairAccessible { get; init; }

	/// <summary>
	/// Whether bicycle transport is available.
	/// </summary>
	public bool? BicycleAccessible { get; init; }

	/// <summary>
	/// Whether the vehicle has low-floor access.
	/// </summary>
	public bool? LowFloor { get; init; }
}

/// <summary>
/// Geographic vehicle position.
/// </summary>
public sealed class GeoPosition
{
	public double Latitude { get; init; }

	public double Longitude { get; init; }
}
