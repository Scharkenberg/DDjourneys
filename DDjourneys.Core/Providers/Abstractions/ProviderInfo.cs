namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>What a data provider can do. The UI offers features only when the selected provider supports them.</summary>
[Flags]
public enum ProviderCapabilities
{
	None = 0,

	/// <summary>Journey search.</summary>
	Journeys = 1,

	/// <summary>Stop and address search.</summary>
	Places = 2,

	/// <summary>"Earlier" and "later" journeys relative to a result.</summary>
	Continuation = 4,

	/// <summary>Live following of a journey with notifications.</summary>
	Tracking = 8,

	/// <summary>Occupancy of vehicles and stops.</summary>
	Occupancy = 16,

	/// <summary>Platform (Steig) and track (Gleis) data.</summary>
	Platforms = 32,

	/// <summary>Routing preferences (modes, transfers, walking, accessibility) are honoured.</summary>
	RoutingPreferences = 64,

	/// <summary>Departure monitor and the course of a vehicle.</summary>
	Departures = 128,

	/// <summary>Route changes, disruptions and network notices.</summary>
	Disruptions = 256,

	/// <summary>Stops near a position, lines of a stop, tariff zones.</summary>
	NetworkInfo = 512,

	/// <summary>Alternatives for a single leg of a journey, and a printable journey.</summary>
	JourneyExtras = 1024
}

/// <summary>
/// Static description of a data provider (one transport authority or API). Names are proper nouns and
/// stay untranslated; the UI localizes capability labels and everything around them.
/// </summary>
/// <param name="Id">Stable identifier, stored in the settings. Never changes once shipped.</param>
/// <param name="Name">Short name shown in lists ("VVO").</param>
/// <param name="FullName">Official name ("Verkehrsverbund Oberelbe").</param>
/// <param name="Region">Group heading in the picker ("Sachsen"); providers with the same region are listed together.</param>
/// <param name="Coverage">Main places served, as proper nouns.</param>
/// <param name="Capabilities">What the provider supports.</param>
public sealed record ProviderInfo(
	string Id,
	string Name,
	string FullName,
	string Region,
	string Coverage,
	ProviderCapabilities Capabilities)
{
	public bool Supports(ProviderCapabilities capability) =>
		(Capabilities & capability) == capability;
}

/// <summary>
/// Implemented by journey and location providers so the services can route a request to the provider
/// the user selected. Providers without a descriptor are always eligible (they cannot be selected away).
/// </summary>
public interface IProviderDescriptor
{
	ProviderInfo Info { get; }
}
