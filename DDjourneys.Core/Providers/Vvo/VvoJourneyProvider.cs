using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Mapping;
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
				"vvo_origin_missing_id");
		}

		if (string.IsNullOrWhiteSpace(query.To.Id))
		{
			return JourneyResult.Failure(
				"vvo_destination_missing_id");
		}

		var request = new VvoTripRequest
		{
			Origin = query.From.Id,
			Destination = query.To.Id,
			Time = query.DateTime,
			IsArrivalTime =
				query.SearchMode == JourneySearchMode.Arrival
		};

		try
		{
			var response =
				await _apiClient.GetTripsAsync(
					request,
					cancellationToken,
					TimeSpan.FromSeconds(
						Math.Clamp(
							query.TimeoutSeconds,
							5,
							60)))
				.ConfigureAwait(false);

			if (response is null)
			{
				return JourneyResult.Failure(
					"vvo_no_response");
			}

			if (response.Routes.Count == 0)
			{
				return JourneyResult.Success(
					Array.Empty<Journey>());
			}

			var journeys =
				VvoJourneyMapper.Map(response);

			return JourneyResult.Success(
				journeys);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (DDjourneys.Core.Api.ApiException ex)
		{
			return JourneyResult.Failure(
				ex.Message);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(
				$"VVO journey mapping failed: {ex}");

			return JourneyResult.Failure(
				"vvo_response_unreadable");
		}
	}
}