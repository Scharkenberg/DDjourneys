namespace DDjourneys.Core.Models;

/// <summary>Real-time state of a departure or of a stop on a run.</summary>
public enum DepartureState
{
	Unknown = 0,

	InTime,

	Delayed,

	Cancelled
}


/// <summary>Where a stop lies relative to the vehicle of a run.</summary>
public enum RunPosition
{
	Unknown = 0,

	Previous,

	Current,

	Next,

	Onward
}


/// <summary>What to list: departures (or arrivals) at one stop.</summary>
public sealed class DepartureQuery
{
	public required Location Stop { get; init; }

	/// <summary>Null: now.</summary>
	public DateTimeOffset? Time { get; init; }

	/// <summary>True: list arrivals at <see cref="Time"/> instead of departures.</summary>
	public bool IsArrival { get; init; }

	public int Limit { get; init; } = 20;

	/// <summary>Modes to list; <see cref="ModeFilter.All"/> and <see cref="ModeFilter.None"/> mean no filter.</summary>
	public ModeFilter Modes { get; init; } = ModeFilter.All;

	public int TimeoutSeconds { get; init; } = 15;
}


/// <summary>One vehicle leaving (or arriving at) a stop.</summary>
public sealed class Departure
{
	/// <summary>Provider id of the departure; names the run (see <c>GetRunAsync</c>).</summary>
	public required string Id { get; init; }

	public required string StopId { get; init; }

	public required TransitLine Line { get; init; }

	public required DateTimeOffset Scheduled { get; init; }

	/// <summary>The time is an arrival (the board was asked for arrivals).</summary>
	public bool IsArrival { get; init; }

	public DateTimeOffset? Realtime { get; init; }

	public string? Platform { get; init; }

	public PlatformKind PlatformKind { get; init; }

	public DepartureState State { get; init; }

	public OccupancyLevel Occupancy { get; init; } = OccupancyLevel.Unknown;

	/// <summary>Ids of the route changes (disruptions, construction) that affect this run.</summary>
	public IReadOnlyList<string> RouteChangeIds { get; init; } = [];

	public bool IsCancelled => State == DepartureState.Cancelled;

	public DateTimeOffset Effective => Realtime ?? Scheduled;

	public TimeSpan? Delay => Realtime is { } real ? real - Scheduled : null;

	public bool HasRouteChanges => RouteChangeIds.Count > 0;
}


/// <summary>A stop's departures.</summary>
public sealed class DepartureBoard
{
	public static DepartureBoard Empty { get; } = new() { StopName = string.Empty };

	public required string StopName { get; init; }

	public string? StopPlace { get; init; }

	public IReadOnlyList<Departure> Departures { get; init; } = [];
}


/// <summary>One stop of a vehicle's run.</summary>
public sealed class RunStop
{
	public required Station Station { get; init; }

	public RunPosition Position { get; init; }

	public DateTimeOffset? Scheduled { get; init; }

	public DateTimeOffset? Realtime { get; init; }

	public DepartureState State { get; init; }

	public OccupancyLevel Occupancy { get; init; } = OccupancyLevel.Unknown;

	public bool IsCancelled => State == DepartureState.Cancelled;

	public DateTimeOffset? Effective => Realtime ?? Scheduled;

	public TimeSpan? Delay =>
		Realtime is { } real && Scheduled is { } plan
			? real - plan
			: null;
}
