using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Requests;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>
/// Provides journey planning using the VVO WebAPI.
/// </summary>
public sealed class VvoJourneyProvider : IJourneyProvider
{
	private readonly VvoApiClient _apiClient;


	public VvoJourneyProvider(
		VvoApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(apiClient);

		_apiClient = apiClient;
	}


	/// <inheritdoc />
	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);


		if (string.IsNullOrWhiteSpace(query.From.Id))
		{
			return JourneyResult.Failure(
				"Origin does not contain a provider location ID.");
		}


		if (string.IsNullOrWhiteSpace(query.To.Id))
		{
			return JourneyResult.Failure(
				"Destination does not contain a provider location ID.");
		}


		var request = new VvoTripRequest
		{
			Origin = query.From.Id,
			Destination = query.To.Id,
			Time = query.DateTime,
			IsArrivalTime = query.SearchMode == JourneySearchMode.Arrival
		};


		var response =
			await _apiClient.GetTripsAsync(
				request,
				cancellationToken)
			.ConfigureAwait(false);


		if (response is null)
		{
			return JourneyResult.Failure(
				"The VVO provider returned no response.");
		}


		/*
         * Mapping is intentionally not done here.
         *
         * Next step:
         *
         * VvoTripResponse
         *        |
         *        v
         * VvoJourneyMapper
         *        |
         *        v
         * JourneyResult
         */


		if (response.Trips.Count == 0)
		{
			return JourneyResult.Success(
				Array.Empty<Journey>());
		}


		return JourneyResult.Failure(
			"VVO trip mapping is not implemented yet.");
	}
}