using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Requests;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>
/// Provides journey planning using the VVO WebAPI.
/// </summary>
public sealed class VvoJourneyProvider :
	IJourneyProvider,
	IJourneyContinuationProvider
{
	private const int MaxContinuationResults = 10;

	private readonly VvoApiClient _apiClient;


	public VvoJourneyProvider(
		VvoApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(
			apiClient);

		_apiClient = apiClient;
	}


	/// <inheritdoc />
	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

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


		var request =
			new VvoTripRequest
			{
				Origin =
					query.From.Id,

				Destination =
					query.To.Id,

				Time =
					query.DateTime,

				IsArrivalTime =
					query.SearchMode == JourneySearchMode.Arrival,

				ShortTermChanges = true,

				StandardSettings =
					CreateStandardSettings(),

				MobilitySettings =
					CreateMobilitySettings()
			};


		try
		{
			VvoTripResponse? response =
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


			IReadOnlyList<Journey> journeys =
				VvoJourneyMapper.Map(
					response);


			return JourneyResult.Success(
				LimitResults(
					journeys,
					query.MaxResults));
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
				$"VVO journey request failed: {ex}");

			return JourneyResult.Failure(
				"vvo_response_unreadable");
		}
	}


	/// <summary>
	/// Requeries VVO and returns the provider-native route corresponding
	/// to a normalized DDjourneys journey.
	///
	/// Schutzengel requires the original VVO Connection object as rawData.
	/// The normalized Journey intentionally does not retain provider-specific
	/// state, so the original connection is rehydrated when tracking starts.
	/// </summary>
	public async Task<
		(VvoRoute Route, string? SessionId, VvoStatus? Status)?>
		GetSchutzengelConnectionAsync(
			Journey target,
			CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			target);

		if (string.IsNullOrWhiteSpace(
			target.From.Id))
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(
			target.To.Id))
		{
			return null;
		}


		DateTimeOffset requestedTime =
			target.Departure
			?? target.Legs
				.FirstOrDefault()
				?.ScheduledDeparture
			?? DateTimeOffset.UtcNow;


		var request =
			new VvoTripRequest
			{
				Origin =
					target.From.Id,

				Destination =
					target.To.Id,

				Time =
					requestedTime,

				IsArrivalTime = false,

				ShortTermChanges = true,

				StandardSettings =
					CreateStandardSettings(),

				MobilitySettings =
					CreateMobilitySettings()
			};


		VvoTripResponse? response =
			await _apiClient.GetTripsAsync(
				request,
				cancellationToken,
				TimeSpan.FromSeconds(15))
			.ConfigureAwait(false);


		if (response is null
			|| response.Routes.Count == 0)
		{
			return null;
		}


		IReadOnlyList<Journey> journeys =
			VvoJourneyMapper.Map(
				response);


		int count =
			Math.Min(
				response.Routes.Count,
				journeys.Count);


		var candidates =
			new List<
				(
					int Index,
					int Score,
					double TimeDeltaSeconds
				)>();


		for (int i = 0; i < count; i++)
		{
			Journey candidate =
				journeys[i];

			int score =
				GetTrackingMatchScore(
					target,
					candidate);

			if (score <= 0)
			{
				continue;
			}


			candidates.Add(
				(
					Index: i,
					Score: score,
					TimeDeltaSeconds:
						GetScheduledTimeDeltaSeconds(
							target,
							candidate)
				));
		}


		if (candidates.Count == 0)
		{
			return null;
		}


		var best =
			candidates
				.OrderByDescending(
					item => item.Score)
				.ThenBy(
					item => item.TimeDeltaSeconds)
				.First();


		VvoRoute route =
			response.Routes[best.Index];


		System.Diagnostics.Debug.WriteLine(
			$"""
			[VVO SCHUTZENGEL]
			Matched RouteId={route.RouteId}
			Score={best.Score}
			TimeDeltaSeconds={best.TimeDeltaSeconds:F0}
			SessionId={response.SessionId}
			""");


		return (
			Route: route,
			SessionId: response.SessionId,
			Status: response.Status);
	}


	/// <inheritdoc />
	public Task<JourneyResult> GetPreviousAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default) =>
		GetAdjacentAsync(
			query,
			currentJourney,
			previous: true,
			count,
			cancellationToken);


	/// <inheritdoc />
	public Task<JourneyResult> GetNextAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default) =>
		GetAdjacentAsync(
			query,
			currentJourney,
			previous: false,
			count,
			cancellationToken);


	private async Task<JourneyResult> GetAdjacentAsync(
		JourneyQuery query,
		Journey currentJourney,
		bool previous,
		int count,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		ArgumentNullException.ThrowIfNull(
			currentJourney);

		if (string.IsNullOrWhiteSpace(
			query.From.Id))
		{
			return JourneyResult.Failure(
				"vvo_origin_missing_id");
		}

		if (string.IsNullOrWhiteSpace(
			query.To.Id))
		{
			return JourneyResult.Failure(
				"vvo_destination_missing_id");
		}

		if (string.IsNullOrWhiteSpace(
			currentJourney.Context))
		{
			return JourneyResult.Failure(
				"vvo_continuation_context_missing");
		}

		count =
			Math.Clamp(
				count,
				1,
				MaxContinuationResults);


		var request =
			new VvoPrevNextRequest
			{
				Origin =
					query.From.Id,

				Destination =
					query.To.Id,

				SessionId =
					currentJourney.Context,

				Time =
					query.DateTime,

				IsArrivalTime =
					query.SearchMode == JourneySearchMode.Arrival,

				ShortTermChanges = true,

				StandardSettings =
					CreateStandardSettings(),

				MobilitySettings =
					CreateMobilitySettings(),

				Previous =
					previous,

				NumberPrevious =
					previous
						? count
						: 0,

				NumberNext =
					previous
						? 0
						: count
			};


		try
		{
			VvoTripResponse? response =
				await _apiClient.GetPreviousNextTripsAsync(
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


			IReadOnlyList<Journey> journeys =
				VvoJourneyMapper.Map(
					response);


			return JourneyResult.Success(
				LimitResults(
					journeys,
					count));
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
				$"VVO continuation request failed: {ex}");

			return JourneyResult.Failure(
				"vvo_response_unreadable");
		}
	}


	private static int GetTrackingMatchScore(
		Journey expected,
		Journey actual)
	{
		if (!string.Equals(
			expected.From.Id,
			actual.From.Id,
			StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(
				expected.To.Id,
				actual.To.Id,
				StringComparison.OrdinalIgnoreCase)
			|| expected.Legs.Count != actual.Legs.Count)
		{
			return 0;
		}


		int score = 10;


		for (int i = 0;
			i < expected.Legs.Count;
			i++)
		{
			JourneyLeg expectedLeg =
				expected.Legs[i];

			JourneyLeg actualLeg =
				actual.Legs[i];


			if (expectedLeg.Mode != actualLeg.Mode
				|| !string.Equals(
					expectedLeg.Line?.Name,
					actualLeg.Line?.Name,
					StringComparison.OrdinalIgnoreCase)
				|| !string.Equals(
					expectedLeg.From.Id,
					actualLeg.From.Id,
					StringComparison.OrdinalIgnoreCase)
				|| !string.Equals(
					expectedLeg.To.Id,
					actualLeg.To.Id,
					StringComparison.OrdinalIgnoreCase))
			{
				return 0;
			}


			score += 10;


			if (!string.IsNullOrWhiteSpace(
				expectedLeg.Id)
				&& !string.IsNullOrWhiteSpace(
					actualLeg.Id)
				&& string.Equals(
					expectedLeg.Id,
					actualLeg.Id,
					StringComparison.Ordinal))
			{
				score += 2;
			}


			if (expectedLeg.Stops.Count > 0
				&& actualLeg.Stops.Count > 0)
			{
				if (expectedLeg.Stops.Count
					!= actualLeg.Stops.Count)
				{
					return 0;
				}


				for (int stopIndex = 0;
					stopIndex < expectedLeg.Stops.Count;
					stopIndex++)
				{
					if (!string.Equals(
						expectedLeg.Stops[stopIndex].Station.Id,
						actualLeg.Stops[stopIndex].Station.Id,
						StringComparison.OrdinalIgnoreCase))
					{
						return 0;
					}
				}


				score += 5;
			}
		}


		return score;
	}


	private static double GetScheduledTimeDeltaSeconds(
		Journey expected,
		Journey actual)
	{
		double totalSeconds = 0;


		int count =
			Math.Min(
				expected.Legs.Count,
				actual.Legs.Count);


		for (int i = 0; i < count; i++)
		{
			JourneyLeg expectedLeg =
				expected.Legs[i];

			JourneyLeg actualLeg =
				actual.Legs[i];


			if (expectedLeg.ScheduledDeparture is { } expectedDeparture
				&& actualLeg.ScheduledDeparture is { } actualDeparture)
			{
				totalSeconds +=
					Math.Abs(
						(expectedDeparture - actualDeparture)
						.TotalSeconds);
			}


			if (expectedLeg.ScheduledArrival is { } expectedArrival
				&& actualLeg.ScheduledArrival is { } actualArrival)
			{
				totalSeconds +=
					Math.Abs(
						(expectedArrival - actualArrival)
						.TotalSeconds);
			}
		}


		return totalSeconds;
	}


	private static VvoStandardSettings CreateStandardSettings()
	{
		return new VvoStandardSettings
		{
			MaxChanges = "Unlimited",
			WalkingSpeed = "Normal",
			FootpathToStop = 5,
			IncludeAlternativeStops = true
		};
	}


	private static VvoMobilitySettings CreateMobilitySettings()
	{
		return new VvoMobilitySettings
		{
			MobilityRestriction = "None",
			SolidStairs = true,
			Escalators = true,
			LeastChange = false,
			Entrance = "Any"
		};
	}


	private static IReadOnlyList<Journey> LimitResults(
		IReadOnlyList<Journey> journeys,
		int maximum)
	{
		if (maximum <= 0
			|| journeys.Count <= maximum)
		{
			return journeys;
		}

		return journeys
			.Take(maximum)
			.ToArray();
	}
}