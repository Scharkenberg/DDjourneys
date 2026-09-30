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
		ArgumentNullException.ThrowIfNull(providers);

		_providers = providers;
	}

	/// <summary>
	/// Searches for journeys matching the supplied query.
	/// </summary>
	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		JourneyResult? lastFailure = null;

		foreach (IJourneyProvider provider in _providers)
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
}