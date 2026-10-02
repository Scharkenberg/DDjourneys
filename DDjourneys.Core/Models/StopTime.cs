namespace DDjourneys.Core.Models;

/// <summary>
/// Represents a station visit within a specific journey leg.
///
/// Unlike Station, this object contains timetable and realtime
/// information for one occurrence of stopping at that station.
/// </summary>
public sealed class StopTime
{
	/// <summary>
	/// Station where this event occurs.
	/// </summary>
	public required Station Station { get; init; }


	/// <summary>
	/// Scheduled arrival time.
	///
	/// Null for the first stop of a leg.
	/// </summary>
	public DateTimeOffset? ScheduledArrival { get; init; }


	/// <summary>
	/// Realtime arrival time.
	///
	/// Null when no realtime data exists.
	/// </summary>
	public DateTimeOffset? RealtimeArrival { get; init; }


	/// <summary>
	/// Scheduled departure time.
	///
	/// Null for the final stop of a leg.
	/// </summary>
	public DateTimeOffset? ScheduledDeparture { get; init; }


	/// <summary>
	/// Realtime departure time.
	///
	/// Null when no realtime data exists.
	/// </summary>
	public DateTimeOffset? RealtimeDeparture { get; init; }


	/// <summary>
	/// Platform or track information.
	/// </summary>
	public string? Platform { get; init; }


	/// <summary>
	/// Whether <see cref="Platform"/> is a platform (Steig) or a track (Gleis).
	/// </summary>
	public PlatformKind PlatformKind { get; init; }


	/// <summary>
	/// Whether this stop has been cancelled.
	/// </summary>
	public bool IsCancelled { get; init; }


	/// <summary>
	/// Current occupancy level at this stop.
	/// </summary>
	public OccupancyLevel Occupancy { get; init; } = OccupancyLevel.Unknown;


	/// <summary>
	/// Effective arrival time using realtime data if available.
	/// </summary>
	public DateTimeOffset? EffectiveArrival =>
		RealtimeArrival ?? ScheduledArrival;


	/// <summary>
	/// Effective departure time using realtime data if available.
	/// </summary>
	public DateTimeOffset? EffectiveDeparture =>
		RealtimeDeparture ?? ScheduledDeparture;


	/// <summary>
	/// Arrival delay compared with timetable.
	/// </summary>
	public TimeSpan? ArrivalDelay =>
		RealtimeArrival.HasValue && ScheduledArrival.HasValue
			? RealtimeArrival.Value - ScheduledArrival.Value
			: null;


	/// <summary>
	/// Departure delay compared with timetable.
	/// </summary>
	public TimeSpan? DepartureDelay =>
		RealtimeDeparture.HasValue && ScheduledDeparture.HasValue
			? RealtimeDeparture.Value - ScheduledDeparture.Value
			: null;
}
