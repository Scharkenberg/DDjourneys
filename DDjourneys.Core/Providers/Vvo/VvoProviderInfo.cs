using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>Description of the VVO provider (Verkehrsverbund Oberelbe, Dresden).</summary>
public static class VvoProviderInfo
{
	public const string Id = "vvo";

	public static ProviderInfo Value { get; } =
		new(
			Id,
			"VVO",
			"Verkehrsverbund Oberelbe",
			"Sachsen",
			"Dresden · Meißen · Pirna · Bautzen · Görlitz",
			ProviderCapabilities.Journeys
			| ProviderCapabilities.Places
			| ProviderCapabilities.Continuation
			| ProviderCapabilities.Tracking
			| ProviderCapabilities.Occupancy
			| ProviderCapabilities.Platforms
			| ProviderCapabilities.RoutingPreferences
			| ProviderCapabilities.Departures
			| ProviderCapabilities.Disruptions
			| ProviderCapabilities.NetworkInfo
			| ProviderCapabilities.JourneyExtras);
}
