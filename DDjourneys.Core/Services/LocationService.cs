using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Services;

/// <summary>
/// Provides location lookup functionality.
/// </summary>
public sealed class LocationService
{
	private readonly ILocationProvider _provider;


	public LocationService(
	ILocationProvider provider)
	{
		ArgumentNullException.ThrowIfNull(provider);

		_provider = provider;
	}


	/// <summary>
	/// Searches for locations matching user input.
	/// </summary>
	public Task<IReadOnlyList<Location>> SearchAsync(
	string query,
	CancellationToken cancellationToken = default,
	TimeSpan? timeout = null)
	{
		return _provider.SearchAsync(
			query,
			cancellationToken,
			timeout);
	}
}
