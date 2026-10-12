namespace DDjourneys.Core.Models;

/// <summary>A park &amp; ride site with its current occupancy, where the operator publishes one.</summary>
/// <param name="Id">Stable identifier of the site (marker ids are namespaced with it).</param>
/// <param name="Name">As the operator names it ("P+R Radebeul Ost").</param>
/// <param name="Total">Spaces in total, when known; 0 means nothing is known.</param>
/// <param name="Free">Spaces free right now, when known.</param>
/// <param name="LiveAt">When the counts were taken; null means the counts are not trustworthy (shown without numbers).</param>
public sealed record ParkingSite(
	string Id,
	string Name,
	double Lat,
	double Lon,
	int Total,
	int Free,
	DateTimeOffset? LiveAt = null)
{
	/// <summary>Whether the counts are live: a site without a live count shows its place, not numbers.</summary>
	public bool HasLive => LiveAt is not null;
}
