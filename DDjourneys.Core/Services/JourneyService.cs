using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>
/// Provides journey search functionality.
/// </summary>
/// <remarks>
/// This service acts as the application-facing entry point for journey
/// planning. It delegates the actual search to registered journey providers.
///
/// Providers are responsible for:
/// - creating provider-specific requests,
/// - communicating with external APIs,
/// - parsing provider responses,
/// - mapping results into DDjourneys domain models.
///
/// Optional provider capabilities, such as adjacent-journey retrieval,
/// are exposed through dedicated capability interfaces.
///
/// This keeps the application independent from individual transport APIs.
/// </remarks>
public sealed class JourneyService
{
	private readonly IEnumerable<IJourneyProvider> _providers;


	/// <summary>
	/// Creates a new journey service.
	/// </summary>
	public JourneyService(
		IEnumerable<IJourneyProvider> providers)
	{
		ArgumentNullException.ThrowIfNull(
			providers);

		_providers = providers;
	}


	/// <summary>
	/// Searches for journeys matching the supplied query.
	/// </summary>
	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		JourneyResult? lastFailure = null;

		foreach (IJourneyProvider provider
			in _providers)
		{
			JourneyResult result =
				await provider.SearchAsync(
					query,
					cancellationToken)
				.ConfigureAwait(false);

			if (result.IsSuccessful)
			{
				return result;
			}

			lastFailure = result;
		}

		return lastFailure
			?? JourneyResult.Failure(
				"journey_no_providers");
	}


	/// <summary>
	/// Gets journeys preceding the supplied journey.
	/// </summary>
	public Task<JourneyResult> GetPreviousAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		ArgumentNullException.ThrowIfNull(
			currentJourney);

		return GetAdjacentAsync(
			query,
			currentJourney,
			previous: true,
			count,
			cancellationToken);
	}


	/// <summary>
	/// Gets journeys following the supplied journey.
	/// </summary>
	public Task<JourneyResult> GetNextAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		ArgumentNullException.ThrowIfNull(
			currentJourney);

		return GetAdjacentAsync(
			query,
			currentJourney,
			previous: false,
			count,
			cancellationToken);
	}


	private async Task<JourneyResult> GetAdjacentAsync(
		JourneyQuery query,
		Journey currentJourney,
		bool previous,
		int count,
		CancellationToken cancellationToken)
	{
		JourneyResult? lastFailure = null;


		foreach (IJourneyProvider provider
			in _providers)
		{
			if (provider
				is not IJourneyContinuationProvider continuationProvider)
			{
				continue;
			}


			JourneyResult result =
				previous
					? await continuationProvider.GetPreviousAsync(
						query,
						currentJourney,
						count,
						cancellationToken)
						.ConfigureAwait(false)
					: await continuationProvider.GetNextAsync(
						query,
						currentJourney,
						count,
						cancellationToken)
						.ConfigureAwait(false);


			if (result.IsSuccessful)
			{
				return result;
			}


			lastFailure = result;
		}


		return lastFailure
			?? JourneyResult.Failure(
				"journey_continuation_not_supported");
	}
}