using DDjourneys.Core.Api;
using DDjourneys.Core.Models;
using DDjourneys.Core.Parsing;

namespace DDjourneys.Core.Services;

/// <summary>
/// Provides journey search functionality.
/// </summary>
public sealed class JourneyService
{
	private readonly ApiClient _apiClient;

	private readonly JourneyParser _parser;


	public JourneyService(
		ApiClient apiClient,
		JourneyParser parser)
	{
		ArgumentNullException.ThrowIfNull(apiClient);
		ArgumentNullException.ThrowIfNull(parser);

		_apiClient = apiClient;
		_parser = parser;
	}


	/// <summary>
	/// Searches for journeys matching the supplied query.
	/// </summary>
	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);


		/*
         * The provider-specific request creation will happen here later.
         *
         * Current flow:
         *
         * JourneyQuery
         *      |
         *      v
         * Provider request
         *      |
         *      v
         * ApiClient
         *      |
         *      v
         * JSON
         *      |
         *      v
         * JourneyParser
         *      |
         *      v
         * JourneyResult
         */


		throw new NotImplementedException(
			"Journey provider integration has not been implemented yet.");
	}
}