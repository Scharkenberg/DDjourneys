using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>
/// Provides location search functionality.
/// </summary>
public interface ILocationProvider
{
	/// <summary>
	/// Searches for locations matching the supplied query.
	/// </summary>
	Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null);
}
