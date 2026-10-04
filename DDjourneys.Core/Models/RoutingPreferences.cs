namespace DDjourneys.Core.Models;

/// <summary>How many transfers a journey may contain.</summary>
public enum MaxTransfers
{
	Unlimited = 0,
	Two,
	One,
	None
}

/// <summary>Assumed walking speed for footpaths and transfers.</summary>
public enum WalkingPace
{
	VerySlow = 0,
	Slow,
	Normal,
	Fast,
	VeryFast
}

/// <summary>Step-free travel needs. The provider maps these to its own mobility profiles.</summary>
public enum AccessibilityNeed
{
	None = 0,
	Medium,
	High
}

/// <summary>Modes of transport a journey may use.</summary>
/// <summary>Step height at the vehicle entrance a passenger can manage.</summary>
public enum EntranceNeed
{
	Any = 0,

	SmallStep,

	NoStep
}


/// <summary>Fare supplements the passenger wants to avoid.</summary>
public enum ExtraChargeFilter
{
	/// <summary>No restriction.</summary>
	Any = 0,

	/// <summary>No journeys with a supplement.</summary>
	None,

	/// <summary>Only local transport (no supplement-bearing long-distance trains).</summary>
	LocalTraffic
}


/// <summary>What the router optimises for (providers with <c>RouteOptimisation</c>).</summary>
public enum RouteOptimisation
{
	Fastest = 0,

	FewestChanges,

	LeastWalking,

	LowestFare
}


[Flags]
public enum ModeFilter
{
	None = 0,
	Tram = 1,
	CityBus = 2,
	IntercityBus = 4,
	SuburbanRailway = 8,
	Train = 16,
	Cableway = 32,
	Ferry = 64,
	HailedSharedTaxi = 128,
	All = Tram | CityBus | IntercityBus | SuburbanRailway | Train | Cableway | Ferry | HailedSharedTaxi
}

/// <summary>
/// What the user wants the planner to respect. Provider-neutral; each provider translates it
/// into its own request. <see cref="Default"/> reproduces the planner's behaviour before these
/// options existed.
/// </summary>
public sealed record RoutingPreferences
{
	public static RoutingPreferences Default { get; } = new();

	public MaxTransfers MaxTransfers { get; init; } = MaxTransfers.Unlimited;

	public WalkingPace Pace { get; init; } = WalkingPace.Normal;

	/// <summary>Longest walk to an alternative stop, minutes.</summary>
	public int FootpathMinutes { get; init; } = 5;

	public bool AlternativeStops { get; init; } = true;

	public ModeFilter Modes { get; init; } = ModeFilter.All;

	public AccessibilityNeed Accessibility { get; init; } = AccessibilityNeed.None;

	public bool AvoidStairs { get; init; }

	public bool AvoidEscalators { get; init; }

	/// <summary>Prefer the journey with the fewest transfers over the fastest one.</summary>
	public bool FewestTransfers { get; init; }

	/// <summary>Required vehicle entrance (VVO <c>entrance</c>).</summary>
	public EntranceNeed Entrance { get; init; } = EntranceNeed.Any;

	/// <summary>Fare supplement filter (VVO <c>extraCharge</c>).</summary>
	public ExtraChargeFilter ExtraCharge { get; init; } = ExtraChargeFilter.Any;

	/// <summary>What to optimise for; only providers that can choose an algorithm honour it.</summary>
	public RouteOptimisation Optimisation { get; init; } = RouteOptimisation.Fastest;
}
