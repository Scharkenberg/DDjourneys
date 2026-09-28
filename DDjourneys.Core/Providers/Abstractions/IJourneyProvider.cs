using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>
/// Provides journey planning functionality.
/// </summary>
public interface IJourneyProvider
{
	/// <summary>
	/// Searches for journeys matching the supplied query.
	/// </summary>
	Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default);
}