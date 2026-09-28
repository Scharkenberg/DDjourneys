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

			IsArrivalTime =
				query.SearchMode == JourneySearchMode.Arrival
		};


		try
		{
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

			System.Diagnostics.Debug.WriteLine(
				$"VVO routes: {response.Routes.Count}");

			foreach (var route in response.Routes)
			{
				System.Diagnostics.Debug.WriteLine(
					$"Partial routes: {route.PartialRoutes.Count}");

				foreach (var partial in route.PartialRoutes)
				{
					System.Diagnostics.Debug.WriteLine(
						$"Stops: {partial.RegularStops.Count}");
				}
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
		catch (Exception ex)
		{
			return JourneyResult.Failure(
				$"VVO journey search failed: {ex.Message}");
		}
	}
}