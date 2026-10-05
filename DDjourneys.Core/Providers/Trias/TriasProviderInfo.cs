using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// The VVO TRIAS interface (VDV 431-2): a second way into the same network, with route optimisation,
/// fares and a router that accepts addresses and points of interest.
/// </summary>
public static class TriasProviderInfo
{
	public const string Id = "trias-vvo";

	public static ProviderInfo Value { get; } =
		new(
			Id,
			"VVO (TRIAS)",
			"Verkehrsverbund Oberelbe, TRIAS interface",
			"Sachsen",
			"Dresden · Meißen · Pirna · Bautzen · Görlitz",
			ProviderCapabilities.Journeys
			| ProviderCapabilities.Places
			| ProviderCapabilities.Platforms
			| ProviderCapabilities.Occupancy
			| ProviderCapabilities.RoutingPreferences
			| ProviderCapabilities.Departures
			| ProviderCapabilities.Fares
			| ProviderCapabilities.RouteOptimisation
			| ProviderCapabilities.LiveVehicles
			| ProviderCapabilities.OpenData,
			IsExperimental: true,
			Center: new MapCenter("Dresden", 51.0504, 13.7373));
}
