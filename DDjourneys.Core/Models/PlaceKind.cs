namespace DDjourneys.Core.Models;

/// <summary>What a <see cref="Location"/> is: a stop, or a place the router walks to.</summary>
public enum PlaceKind
{
	Stop,
	Address,
	Poi,

	/// <summary>A bare position.</summary>
	Coordinate,

	/// <summary>A district or locality: found by the search, but not a point to route to.</summary>
	Area
}


/// <summary>Which kinds of places a search may return.</summary>
[Flags]
public enum PlaceKinds
{
	None = 0,
	Stops = 1,
	Addresses = 2,
	Pois = 4,
	All = Stops | Addresses | Pois
}


public static class PlaceKindExtensions
{
	/// <summary>The search flag that admits <paramref name="kind"/>; areas and bare positions are never searched for.</summary>
	public static PlaceKinds ToFlag(this PlaceKind kind) =>
		kind switch
		{
			PlaceKind.Stop => PlaceKinds.Stops,
			PlaceKind.Address => PlaceKinds.Addresses,
			PlaceKind.Poi => PlaceKinds.Pois,
			_ => PlaceKinds.None
		};
}
