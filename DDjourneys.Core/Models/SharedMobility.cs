namespace DDjourneys.Core.Models;

/// <summary>A shared-bike station with its live availability, where the operator publishes one.</summary>
/// <param name="Operator">Display name of the operator ("MOBIbike"); marker ids are namespaced with it.</param>
/// <param name="StationId">The GBFS station_id, as the operator spells it.</param>
/// <param name="Bikes">Bikes free to take right now (vehicle-type split is a later extra); 0 also stands for "unknown" when the status feed failed.</param>
/// <param name="Docks">Docks free to return to, when the operator says.</param>
/// <param name="IsRenting">Whether the station rents at all right now; null when unknown.</param>
/// <param name="AppUri">The operator's rental app for this station (rental_uris.android); opened only when installed.</param>
/// <param name="WebUri">The web rental page (rental_uris.web): the fallback when the app is missing.</param>
/// <param name="Website">The operator's own page: the last resort for a rental link.</param>
/// <param name="UpdatedAt">When the station last reported (last_reported).</param>
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
	DateTimeOffset? UpdatedAt = null);
