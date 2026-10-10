namespace DDjourneys.Core.Models;

/// <summary>One run of a vehicle as a provider can describe it today.</summary>
public sealed record RunDetail(IReadOnlyList<RunStop> Stops)
{
	/// <summary>Where the vehicle is according to the provider (TRIAS <c>CurrentPosition</c>), when it says.</summary>
	public GeoPosition? Vehicle { get; init; }

	/// <summary>The days the run takes place on, when the provider says.</summary>
	public OperatingDays? OperatingDays { get; init; }

	/// <summary>The real line of the route between the run's stops (the provider's map data), when it
	/// has one; empty otherwise.</summary>
	public IReadOnlyList<(double Latitude, double Longitude)> Path { get; init; } = [];
}
