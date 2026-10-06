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
	JourneyExtras = 1024,

	/// <summary>Live positions of vehicles.</summary>
	LiveVehicles = 2048,

	/// <summary>Open data of the city: stop accessibility, service points.</summary>
	OpenData = 4096,

	/// <summary>Fares and tickets of a journey.</summary>
	Fares = 8192,

	/// <summary>The router can optimise for fastest, fewest changes, least walking or lowest fare.</summary>
	RouteOptimisation = 16384,

	/// <summary>Fares are priced for the traveller category the passenger names (youth, child, senior).</summary>
	PassengerFares = 32768,

	/// <summary>The router honours the walking time to a stop and plans from and to nearby stops.</summary>
	WalkToStops = 65536,

	/// <summary>The router can leave out journeys with a fare supplement.</summary>
	SupplementFilter = 131072
}

/// <summary>A place a map can start at: the central city of a provider's area.</summary>
public sealed record MapCenter(string Name, double Latitude, double Longitude, int Zoom = 12);

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
/// <param name="IsExperimental">Works, but not to the standard of the others; the UI says so.</param>
/// <param name="Center">The central city of the area served: where a map without any other hint starts.</param>
public sealed record ProviderInfo(
	string Id,
	string Name,
	string FullName,
	string Region,
	string Coverage,
	ProviderCapabilities Capabilities,
	bool IsExperimental = false,
	MapCenter? Center = null)
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
