using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>
/// A source of park &amp; ride sites with live occupancy (an open-data file, not a journey API): the whole
/// list in one answer, because there are only a few dozen sites in the region.
/// </summary>
public interface IParkingProvider
{
	/// <summary>All sites the source knows, with their current free counts.</summary>
	Task<IReadOnlyList<ParkingSite>> GetSitesAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default);
}
