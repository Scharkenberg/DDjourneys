namespace DDjourneys.Core.Models;

/// <summary>What a shared vehicle is, as far as the passenger cares.</summary>
public enum SharedKind
{
	Bike,
	EBike,
	CargoBike,
	Scooter
}

/// <summary>A shared-bike station with its live availability, where the operator publishes one.</summary>
/// <param name="Operator">Display name of the operator ("MOBIbike").</param>
/// <param name="StationId">The GBFS station_id, as the operator spells it.</param>
/// <param name="Bikes">Bikes free to take right now; 0 also stands for "unknown" while <paramref name="IsRenting"/> is null (no status).</param>
/// <param name="Docks">Docks free to return to, when the operator says (physical stations only).</param>
/// <param name="IsRenting">Whether the station rents at all right now; null when unknown.</param>
/// <param name="AppUri">The operator's rental link for this station (rental_uris.android), opened in the operator's app.</param>
/// <param name="WebUri">The web rental page (rental_uris.web): the fallback without the app.</param>
/// <param name="Website">The operator's own page: the last resort for a rental link.</param>
/// <param name="UpdatedAt">When the station last reported (last_reported).</param>
/// <param name="OperatorId">Stable operator key ("nextbike"); marker ids are namespaced with it.</param>
/// <param name="IsVirtual">No docks: an area to leave the bike in (MOBIbike's virtual stations), not a MOBIpunkt.</param>
/// <param name="Capacity">Docks in total, for a physical station.</param>
/// <param name="EBikes">Of <paramref name="Bikes"/>, how many are electric (from vehicle_types_available).</param>
/// <param name="AppPackage">The operator's Android app ("de.nextbike"): rental links open there.</param>
/// <param name="CargoBikes">Of <paramref name="Bikes"/>, how many are cargo bikes.</param>
public sealed record SharedStation(
	string Operator,
	string StationId,
	string Name,
	double Lat,
	double Lon,
	int Bikes,
	int? Docks = null,
	bool? IsRenting = null,
	Uri? AppUri = null,
	Uri? WebUri = null,
	Uri? Website = null,
	DateTimeOffset? UpdatedAt = null,
	string OperatorId = "",
	bool IsVirtual = false,
	int? Capacity = null,
	int EBikes = 0,
	string? AppPackage = null,
	int CargoBikes = 0)
{
	/// <summary>Whether the counts come from a live status (without one, 0 bikes means "unknown").</summary>
	public bool HasStatus => IsRenting is not null || UpdatedAt is not null;
}

/// <summary>A vehicle standing free (not at a station): a Lime e-bike, a MOBIbike left in the street.</summary>
/// <param name="RangeMeters">How far its battery carries it, for electric vehicles that say.</param>
/// <param name="AppPackage">The operator's Android app: renting opens there.</param>
public sealed record SharedVehicle(
	string OperatorId,
	string Operator,
	string Id,
	double Lat,
	double Lon,
	SharedKind Kind,
	int? RangeMeters = null,
	Uri? AppUri = null,
	Uri? WebUri = null,
	Uri? Website = null,
	string? AppPackage = null,
	DateTimeOffset? UpdatedAt = null);
