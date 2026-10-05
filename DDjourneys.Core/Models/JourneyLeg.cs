namespace DDjourneys.Core.Models;

/// <summary>
/// Represents one segment of a complete journey.
///
/// Examples:
/// - walking to a stop
/// - riding a tram
/// - riding a train
/// - transfer movement
/// </summary>
public sealed class JourneyLeg
{
	/// <summary>
	/// Type of movement performed during this leg.
	/// </summary>
	public required TransitMode Mode { get; init; }

	/// <summary>
	/// Origin stop/location.
	/// </summary>
	public required Station From { get; init; }

	/// <summary>
	/// Destination stop/location.
	/// </summary>
	public required Station To { get; init; }

	/// <summary>
	/// Stops visited during this leg.
	///
	/// Empty for walking legs.
	/// Usually contains intermediate stops for public transport.
	/// </summary>
	public IReadOnlyList<StopTime> Stops { get; init; }
		= Array.Empty<StopTime>();

	/// <summary>
	/// Geometry of this movement segment in WGS84 coordinates.
	/// </summary>
	public IReadOnlyList<(double Latitude, double Longitude)> Path { get; init; }
		= [];

	/// <summary>
	/// Passenger-visible line information.
	///
	/// Null for walking.
	/// </summary>
	public TransitLine? Line { get; init; }

	/// <summary>The days the service runs on, when the provider says (TRIAS <c>OperatingDays</c>).</summary>
	public OperatingDays? OperatingDays { get; init; }

	/// <summary>
	/// Physical vehicle information if supplied.
	/// </summary>
	public Vehicle? Vehicle { get; init; }

	/// <summary>
	/// Scheduled departure time.
	/// </summary>
	public DateTimeOffset? ScheduledDeparture { get; init; }

	/// <summary>
	/// Realtime departure time.
	///
	/// Null means no realtime update was supplied.
	/// </summary>
	public DateTimeOffset? RealtimeDeparture { get; init; }

	/// <summary>
	/// Scheduled arrival time.
	/// </summary>
	public DateTimeOffset? ScheduledArrival { get; init; }

	/// <summary>
	/// Realtime arrival time.
	///
	/// Null means no realtime update was supplied.
	/// </summary>
	public DateTimeOffset? RealtimeArrival { get; init; }

	/// <summary>
	/// Platform information at departure.
	/// </summary>
	public string? DeparturePlatform { get; init; }

	/// <summary>
	/// Platform (Steig) or track (Gleis) at departure.
	/// </summary>
	public PlatformKind DeparturePlatformKind { get; init; }

	/// <summary>
	/// Platform information at arrival.
	/// </summary>
	public string? ArrivalPlatform { get; init; }

	/// <summary>
	/// Platform (Steig) or track (Gleis) at arrival.
	/// </summary>
	public PlatformKind ArrivalPlatformKind { get; init; }

	/// <summary>
	/// Whether this leg has been cancelled.
	/// </summary>
	public bool IsCancelled { get; init; }

	/// <summary>
	/// Additional provider messages.
	///
	/// Examples:
	/// "construction work"
	/// "replacement bus service"
	/// </summary>
	public IReadOnlyList<string> Notices { get; init; }
		= Array.Empty<string>();


	/// <summary>
	/// True for a scheduled public-transport ride; false for walking, taxi and on-demand legs
	/// (see <see cref="TransitModeExtensions.IsRide"/>).
	/// </summary>
	public bool IsRide => Mode.IsRide();


	/// <summary>
	/// Effective departure time using realtime data when available.
	/// </summary>
	public DateTimeOffset? EffectiveDeparture
	{
		get
		{
			return RealtimeDeparture
				?? (ScheduledDeparture != default
					? ScheduledDeparture
					: null);
		}
	}


	/// <summary>
	/// Provider-independent identifier of this movement segment.
	/// </summary>
	public string? Id { get; init; }

	/// <summary>The provider's own object this leg was mapped from (diagnostics only).</summary>
	public object? ProviderData { get; init; }

	/// <summary>
	/// Effective arrival time using realtime data when available.
	/// </summary>
	public DateTimeOffset? EffectiveArrival
	{
		get
		{
			return RealtimeArrival
				?? (ScheduledArrival != default
					? ScheduledArrival
					: null);
		}
	}


	/// <summary>
	/// Gets the delay compared with the timetable.
	/// </summary>
	public TimeSpan? DepartureDelay =>
		RealtimeDeparture.HasValue
			? RealtimeDeparture.Value - ScheduledDeparture
			: null;


	/// <summary>
	/// Gets the delay compared with the timetable.
	/// </summary>
	public TimeSpan? ArrivalDelay =>
		RealtimeArrival.HasValue
			? RealtimeArrival.Value - ScheduledArrival
			: null;
}