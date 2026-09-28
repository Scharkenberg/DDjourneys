namespace DDjourneys.Core.Models;

/// <summary>
/// Represents a complete route from origin to destination.
/// </summary>
public sealed class Journey
{
	/// <summary>
	/// Individual movement segments making up this journey.
	/// </summary>
	public required IReadOnlyList<JourneyLeg> Legs { get; init; }


	/// <summary>
	/// Original search origin.
	/// </summary>
	public required Station From { get; init; }


	/// <summary>
	/// Final destination.
	/// </summary>
	public required Station To { get; init; }


	/// <summary>
	/// Optional provider identifier.
	///
	/// Some journey APIs provide stable identifiers.
	/// Others do not.
	/// </summary>
	public string? Id { get; init; }


	/// <summary>
	/// Provider-specific continuation context.
	///
	/// Used for requests like:
	/// "show later journeys"
	/// "refresh this journey"
	/// </summary>
	public string? Context { get; init; }


	/// <summary>
	/// Additional messages applying to the entire journey.
	/// </summary>
	public IReadOnlyList<string> Notices { get; init; }
		= Array.Empty<string>();


	/// <summary>
	/// Earliest departure of the journey.
	/// </summary>
	public DateTimeOffset Departure =>
		Legs.Count == 0
			? default
			: Legs[0].EffectiveDeparture;


	/// <summary>
	/// Final arrival of the journey.
	/// </summary>
	public DateTimeOffset Arrival =>
		Legs.Count == 0
			? default
			: Legs[^1].EffectiveArrival;


	/// <summary>
	/// Total journey duration.
	/// </summary>
	public TimeSpan Duration =>
		Arrival - Departure;


	/// <summary>
	/// Number of transfers.
	/// </summary>
	public int TransferCount =>
		Math.Max(
			0,
			Legs.Count(leg => IsPublicTransport(leg)) - 1);


	/// <summary>
	/// Whether any leg is delayed.
	/// </summary>
	public bool HasDelay =>
		Legs.Any(leg =>
			leg.DepartureDelay is { } departureDelay && departureDelay != TimeSpan.Zero
			||
			leg.ArrivalDelay is { } arrivalDelay && arrivalDelay != TimeSpan.Zero);


	/// <summary>
	/// Whether any part of the journey has been cancelled.
	/// </summary>
	public bool IsCancelled =>
		Legs.Any(leg => leg.IsCancelled);


	private static bool IsPublicTransport(JourneyLeg leg)
	{
		return leg.Mode != TransitMode.Walking;
	}
}