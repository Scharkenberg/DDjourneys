using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Services;

/// <summary>
/// Interface for location lookup and GPS functionality.
/// </summary>
public interface ILocationService
{
	/// <summary>
	/// Searches for locations matching user input.
	/// </summary>
	Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null);

	/// <summary>
	/// Searches for locations near the given coordinates.
	/// </summary>
	Task<IReadOnlyList<Location>> SearchByCoordinatesAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null);

	/// <summary>
	/// Gets the current device location using GPS.
	/// </summary>
	Task<Location?> GetCurrentLocationAsync();
}
