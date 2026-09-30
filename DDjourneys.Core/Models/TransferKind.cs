namespace DDjourneys.Core.Models;

/// <summary>
/// Describes the nature of a non-transit movement or interruption
/// between journey legs.
/// </summary>
public enum TransferKind
{
	/// <summary>
	/// Unknown transfer type.
	/// </summary>
	Unknown,

	/// <summary>
	/// Same platform or immediate interchange.
	/// </summary>
	SameStop,

	/// <summary>
	/// Requires platform/track change.
	/// </summary>
	PlatformChange,

	/// <summary>
	/// Waiting for a connecting service at the same location.
	/// </summary>
	Waiting,


	/// <summary>
	/// Walk between locations, platforms, stops, or stations.
	/// </summary>
	Walk,


	/// <summary>
	/// Remaining inside the same vehicle while the route continues.
	/// </summary>
	StayInVehicle,


	/// <summary>
	/// Accessibility-related movement such as elevators,
	/// ramps, or assistance paths.
	/// </summary>
	Accessibility
}