namespace DDjourneys.Core.Models;

/// <summary>One run of a vehicle as a provider can describe it today.</summary>
public sealed record RunDetail(IReadOnlyList<RunStop> Stops)
{
	/// <summary>Where the vehicle is according to the provider (TRIAS <c>CurrentPosition</c>), when it says.</summary>
	public GeoPosition? Vehicle { get; init; }

	/// <summary>The days the run takes place on, when the provider says.</summary>
	public OperatingDays? OperatingDays { get; init; }
}
