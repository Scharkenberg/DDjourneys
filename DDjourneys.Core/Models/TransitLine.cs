namespace DDjourneys.Core.Models;

/// <summary>
/// Represents a passenger-visible public transport service.
/// 
/// Examples:
/// Tram 3
/// Bus 66
/// S-Bahn S1
/// Regional train RE50
/// </summary>
public sealed class TransitLine
{
	/// <summary>
	/// Public line identifier.
	/// 
	/// Examples:
	/// "3"
	/// "S1"
	/// "RE50"
	/// </summary>
	public required string Name { get; init; }

	/// <summary>
	/// Type of transport used by this line.
	/// </summary>
	public required TransitMode Mode { get; init; }

	/// <summary>
	/// Transport operator.
	/// 
	/// Examples:
	/// DVB
	/// VVO
	/// DB Regio
	/// </summary>
	public string? Operator { get; init; }

	/// <summary>
	/// Optional destination shown to passengers.
	/// 
	/// Example:
	/// "Pirna"
	/// "Coswig"
	/// "Dresden Flughafen"
	/// </summary>
	public string? Destination { get; init; }

	/// <summary>
	/// Optional direction identifier from the provider.
	/// 
	/// Some APIs provide a stable direction ID,
	/// others only provide a display name.
	/// </summary>
	public string? DirectionId { get; init; }

	public override string ToString()
	{
		return Mode switch
		{
			TransitMode.Unknown => Name,
			_ => $"{Mode} {Name}"
		};
	}
}

/// <summary>
/// Standardized transport categories used by DDjourneys.
/// </summary>
public enum TransitMode
{
	Unknown = 0,

	Walk,

	Bus,

	Tram,

	Subway,

	SuburbanRail,

	RegionalTrain,

	LongDistanceTrain,

	Ferry,

	CableCar,

	Taxi,

	OnDemand
}