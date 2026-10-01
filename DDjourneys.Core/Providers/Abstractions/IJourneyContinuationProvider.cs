using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>
/// Optional provider capability for retrieving adjacent journeys
/// from an existing journey search context.
/// </summary>
public interface IJourneyContinuationProvider
{
	/// <summary>
	/// Gets earlier journeys than the supplied one.
	/// </summary>
	Task<JourneyResult> GetPreviousAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default);


	/// <summary>
	/// Gets later journeys than the supplied one.
	/// </summary>
	Task<JourneyResult> GetNextAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default);
}