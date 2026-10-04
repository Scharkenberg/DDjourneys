namespace DDjourneys.Core.Models;

/// <summary>One ticket the provider quotes for a journey.</summary>
public sealed class JourneyFare
{
	public required string Name { get; init; }

	public decimal? Price { get; init; }

	/// <summary>ISO 4217 code ("EUR").</summary>
	public string Currency { get; init; } = "EUR";

	/// <summary>Zones, validity or other words of the provider.</summary>
	public string? Description { get; init; }
}
