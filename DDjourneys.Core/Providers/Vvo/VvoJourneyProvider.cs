using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Requests;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>
/// Provides journey planning using the VVO WebAPI.
/// </summary>
public sealed partial class VvoJourneyProvider :
	IJourneyProvider,
	IJourneyContinuationProvider,
	IJourneyExtrasProvider,
	IProviderDescriptor
{
	private const int MaxContinuationResults = 10;

	private readonly VvoApiClient _apiClient;


	/// <inheritdoc />
	public ProviderInfo Info =>
		VvoProviderInfo.Value;


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

		if (CheckEndpoints(
			query.From,
			query.To,
			query.Via) is { } unsuitable)
		{
			return unsuitable;
		}

		Location? viaStop = await ResolveViaStopAsync(query, cancellationToken).ConfigureAwait(false);
		if (query.Via is not null && viaStop is null)
		{
			return JourneyResult.NotSuitable("vvo_via_stop_unavailable");
		}


		var request =
			new VvoTripRequest
			{
				Origin =
					query.From.Id ?? string.Empty,

				Destination =
					query.To.Id ?? string.Empty,

				Time =
					query.DateTime,

				IsArrivalTime =
					query.SearchMode == JourneySearchMode.Arrival,

				ShortTermChanges = true,

				Via = viaStop?.Id,

				StandardSettings =
					CreateStandardSettings(query.Routing),

				MobilitySettings =
					CreateMobilitySettings(query.Routing)
			};


		try
		{
			VvoTripResponse? response =
				await _apiClient.GetTripsAsync(
					request,
					TimeSpan.FromSeconds(
						Math.Clamp(
							query.TimeoutSeconds,
							5,
							60)),
					cancellationToken)
				.ConfigureAwait(false);


			if (response is null)
			{
				return JourneyResult.Failure(
					"vvo_no_response");
			}


			// A successful answer without routes is a result in its own right (Outcome Empty),
			// not a failure: the service was reached and has nothing for this query.
			if (response.Routes.Count == 0)
			{
				return JourneyResult.Success(
					[]);
			}


			IReadOnlyList<Journey> journeys =
				VvoJourneyMapper.Map(
					response,
					query.From,
					query.To,
					query.DateTime,
					query.SearchMode == JourneySearchMode.Arrival);

			RememberRouting(journeys, query.Routing);


			// Everything the service offered: the journey service trims to the requested number,
			// from the right end for the search mode ("arrive by" keeps the latest arrivals).
			return JourneyResult.Success(
				journeys);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (DDjourneys.Core.Api.ApiException ex)
		{
			return Failed(ex);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"VVO journey request failed: {ex}");

			return JourneyResult.Failure(
				"vvo_response_unreadable",
				ex.Message);
		}
	}

	private async Task<Location?> ResolveViaStopAsync(JourneyQuery query, CancellationToken cancellationToken)
	{
		if (query.Via is not { } via)
		{
			return null;
		}

		if (via.IsStation)
		{
			return via;
		}

		if (via.Latitude is not { } latitude || via.Longitude is not { } longitude
			|| !VvoCoordinateConverter.TryToGk4(latitude, longitude, out (double Easting, double Northing) gk4))
		{
			return null;
		}

		try
		{
			VvoPointResponse? points = await _apiClient.FindPointsByCoordinatesAsync(
				gk4.Easting, gk4.Northing,
				TimeSpan.FromSeconds(Math.Clamp(query.TimeoutSeconds, 5, 60)),
				cancellationToken).ConfigureAwait(false);

			return points?.Points
				.Where(point => point.Kind == PlaceKind.Stop && point.IsStop && !string.IsNullOrWhiteSpace(point.Id))
				.Select(VvoLocationProvider.Map)
				.FirstOrDefault();
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"VVO via stop lookup failed: {ex.Message}");
			return null;
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
			?? (target.Legs.Count > 0
				? target.Legs[0].ScheduledDeparture
				: null)
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
					CreateStandardSettings(RoutingOf(target)),

				MobilitySettings =
					CreateMobilitySettings(RoutingOf(target))
			};


		VvoTripResponse? response =
			await _apiClient.GetTripsAsync(
				request,
				TimeSpan.FromSeconds(15),
				cancellationToken)
			.ConfigureAwait(false);


		if (response is null
			|| response.Routes.Count == 0)
		{
			return null;
		}


		IReadOnlyList<Journey> journeys =
			VvoJourneyMapper.Map(
				response,
				target.Origin is { } o ? ToLocation(o) : null,
				target.Destination is { } d ? ToLocation(d) : null);


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


		// Strict first (same stops); then lenient (same legs and times, stops may have changed in real time).
		for (int pass = 0; pass < 2 && candidates.Count == 0; pass++)
		{
			bool strict = pass == 0;

			for (int i = 0; i < count; i++)
			{
				Journey candidate =
					journeys[i];

				int score =
					GetTrackingMatchScore(
						target,
						candidate,
						strict);

				double delta =
					GetScheduledTimeDeltaSeconds(
						target,
						candidate);

				if (score <= 0
					|| (!strict && delta > 300 * Math.Max(1, target.Legs.Count)))
				{
					continue;
				}

				candidates.Add(
					(
						Index: i,
						Score: score,
						TimeDeltaSeconds: delta
					));
			}
		}


		if (candidates.Count == 0)
		{
			DiagnosticLog.Write(
				$"[VVO SCHUTZENGEL] No match among {count} routes for {target.From.Id}->{target.To.Id} at {requestedTime:u} (legs={target.Legs.Count}).");

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


		DiagnosticLog.Write(
			$"""
			[VVO SCHUTZENGEL]
			Matched RouteId={route.RouteId}
			Score={best.Score}
			TimeDeltaSeconds={best.TimeDeltaSeconds:F0}
			SessionId={response.SessionId}
			""");


		return (
			Route: route,
			response.SessionId,
			response.Status);
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

		if (CheckEndpoints(
			query.From,
			query.To,
			query.Via is { IsStation: false } ? null : query.Via) is { } unsuitable)
		{
			return unsuitable;
		}

		// The session id of the original search is the only way to continue it; a journey from
		// another provider (or one without a session) cannot be continued here.
		if (!string.IsNullOrWhiteSpace(currentJourney.ProviderId)
			&& !string.Equals(
				currentJourney.ProviderId,
				VvoProviderInfo.Id,
				StringComparison.OrdinalIgnoreCase))
		{
			return JourneyResult.NotSuitable(
				"vvo_continuation_other_provider");
		}

		if (string.IsNullOrWhiteSpace(
			currentJourney.Context))
		{
			return JourneyResult.NotSuitable(
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
					query.From.Id ?? string.Empty,

				Destination =
					query.To.Id ?? string.Empty,

				SessionId =
					currentJourney.Context,

				Time =
					query.DateTime,

				IsArrivalTime =
					query.SearchMode == JourneySearchMode.Arrival,

				ShortTermChanges = true,

				Via = query.Via?.Id,

				StandardSettings =
					CreateStandardSettings(query.Routing),

				MobilitySettings =
					CreateMobilitySettings(query.Routing),

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
					TimeSpan.FromSeconds(
						Math.Clamp(
							query.TimeoutSeconds,
							5,
							60)),
					cancellationToken)
				.ConfigureAwait(false);


			if (response is null)
			{
				return JourneyResult.Failure(
					"vvo_no_response");
			}


			if (response.Routes.Count == 0)
			{
				return JourneyResult.Success(
					[]);
			}


			IReadOnlyList<Journey> journeys =
				VvoJourneyMapper.Map(
					response,
					query.From,
					query.To,
					query.DateTime,
					query.SearchMode == JourneySearchMode.Arrival);

			RememberRouting(journeys, query.Routing);

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
			return Failed(ex);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"VVO continuation request failed: {ex}");

			return JourneyResult.Failure(
				"vvo_response_unreadable",
				ex.Message);
		}
	}


	/// <inheritdoc />
	public async Task<JourneyResult> GetLegAlternativeAsync(
		JourneyQuery query,
		Journey journey,
		int legIndex,
		bool previous,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		if (CheckEndpoints(
			query.From,
			query.To,
			query.Via) is { } unsuitable)
		{
			return unsuitable;
		}

		if (!IsVvoJourney(journey)
			|| string.IsNullOrWhiteSpace(journey.Context)
			|| string.IsNullOrWhiteSpace(journey.Id))
		{
			return JourneyResult.NotSuitable(
				"vvo_continuation_context_missing");
		}

		if (legIndex < 0
			|| legIndex >= journey.Legs.Count
			|| string.IsNullOrWhiteSpace(journey.Legs[legIndex].Id))
		{
			return JourneyResult.NotSuitable(
				"vvo_leg_missing_id");
		}

		var request =
			new VvoPrevNextMoveRequest
			{
				Origin = query.From.Id!,
				Destination = query.To.Id!,
				SessionId = journey.Context,
				RouteId = journey.Id,
				PartialRouteId = journey.Legs[legIndex].Id!,
				Time = query.DateTime,
				Via = query.Via is { IsStation: true } ? query.Via.Id : null,
				StandardSettings = CreateStandardSettings(query.Routing),
				MobilitySettings = CreateMobilitySettings(query.Routing),
				Previous = previous
			};

		try
		{
			VvoTripResponse? response =
				await _apiClient.GetLegAlternativeAsync(
					request,
					TimeSpan.FromSeconds(
						Math.Clamp(
							query.TimeoutSeconds,
							5,
							60)),
					cancellationToken)
				.ConfigureAwait(false);

			if (response is null)
			{
				return JourneyResult.Failure(
					"vvo_no_response");
			}

			if (response.Routes.Count == 0)
			{
				return JourneyResult.Success(
					[]);
			}

			IReadOnlyList<Journey> journeys =
				VvoJourneyMapper.Map(
					response,
					query.From,
					query.To,
					query.DateTime,
					query.SearchMode == JourneySearchMode.Arrival);

			RememberRouting(journeys, query.Routing);

			return JourneyResult.Success(
				journeys);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (DDjourneys.Core.Api.ApiException ex)
		{
			return Failed(ex);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write(
				$"VVO leg alternative request failed: {ex}");

			return JourneyResult.Failure(
				"vvo_response_unreadable",
				ex.Message);
		}
	}


	/// <inheritdoc />
	public Uri? GetJourneyDocumentUri(
		JourneyQuery query,
		Journey journey)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		if (CheckEndpoints(
			query.From,
			query.To,
			query.Via) is not null
			|| !IsVvoJourney(journey)
			|| string.IsNullOrWhiteSpace(journey.Context)
			|| string.IsNullOrWhiteSpace(journey.Id))
		{
			return null;
		}

		return VvoApiClient.BuildTripPdfUri(
			journey.Id,
			journey.Context,
			query.From.Id!,
			query.To.Id!,
			query.DateTime,
			query.SearchMode == JourneySearchMode.Arrival,
			query.Via?.Id,
			CreateStandardSettings(query.Routing),
			CreateMobilitySettings(query.Routing));
	}


	// A VVO session id looks like "367417461:efa4".
	[System.Text.RegularExpressions.GeneratedRegex(@"^\d+:[A-Za-z0-9]+$")]
	private static partial System.Text.RegularExpressions.Regex SessionShape();


	/// <inheritdoc />
	public async Task<JourneyDocument?> GetJourneyDocumentAsync(
		JourneyQuery query,
		Journey journey,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		DiagnosticLog.Write(
			$"[VVO PDF] journey Id={journey.Id} SessionId={journey.Context} provider={journey.ProviderId} " +
			$"from={query.From.Id} to={query.To.Id} via={query.Via?.Id} time={query.DateTime:O} arrival={query.SearchMode == JourneySearchMode.Arrival}");

		if (string.IsNullOrWhiteSpace(journey.Context)
			|| !SessionShape().IsMatch(journey.Context))
		{
			DiagnosticLog.Write(
				$"[VVO PDF] the session id '{journey.Context}' does not look like a VVO session id (digits, colon, letters); asking anyway");
		}

		if (CheckEndpoints(
			query.From,
			query.To,
			query.Via) is not null
			|| !IsVvoJourney(journey)
			|| string.IsNullOrWhiteSpace(journey.Context)
			|| string.IsNullOrWhiteSpace(journey.Id))
		{
			DiagnosticLog.Write("[VVO PDF] not asked: endpoints, provider, session or route id missing");

			return null;
		}

		var attempts =
			VvoApiClient.BuildTripPdfAttempts(
				journey.Id,
				journey.Context,
				query.From.Id!,
				query.To.Id!,
				query.DateTime,
				query.SearchMode == JourneySearchMode.Arrival,
				query.Via?.Id,
				CreateStandardSettings(query.Routing),
				CreateMobilitySettings(query.Routing));

		byte[]? pdf =
			await _apiClient
				.DownloadTripPdfAsync(
					attempts,
					TimeSpan.FromSeconds(
						Math.Clamp(
							query.TimeoutSeconds,
							5,
							60)),
					cancellationToken)
				.ConfigureAwait(false);

		return pdf is null
			? null
			: new JourneyDocument(
				pdf,
				$"journey-{journey.Id}.pdf");
	}


	private static bool IsVvoJourney(
		Journey journey) =>
		string.IsNullOrWhiteSpace(journey.ProviderId)
		|| string.Equals(
			journey.ProviderId,
			VvoProviderInfo.Id,
			StringComparison.OrdinalIgnoreCase);


	/// <summary>
	/// The provider only answers for places it issued. A place without a provider id (stored before
	/// ids existed, or built by hand) is not held against it. VVO's PointFinder ids can identify
	/// stops, addresses, POIs or coordinate points.
	/// </summary>
	private static JourneyResult? CheckEndpoints(
		Location from,
		Location to,
		Location? via = null)
	{
		if (via is not null)
		{
			if (!IsIssuedByVvo(via))
			{
				return JourneyResult.NotSuitable(
					"vvo_endpoint_other_provider");
			}

			if (!via.IsRoutable || string.IsNullOrWhiteSpace(via.Id))
			{
				return JourneyResult.NotSuitable(
					"vvo_via_missing_id");
			}
		}

		if (!IsIssuedByVvo(from)
			|| !IsIssuedByVvo(to))
		{
			return JourneyResult.NotSuitable(
				"vvo_endpoint_other_provider");
		}

		if (!from.IsRoutable || string.IsNullOrWhiteSpace(from.Id))
		{
			return JourneyResult.NotSuitable(
				"vvo_origin_missing_id");
		}

		if (!to.IsRoutable || string.IsNullOrWhiteSpace(to.Id))
		{
			return JourneyResult.NotSuitable(
				"vvo_destination_missing_id");
		}

		return null;
	}


	private static bool IsIssuedByVvo(
		Location location) =>
		string.IsNullOrWhiteSpace(location.ProviderId)
		|| string.Equals(
			location.ProviderId,
			VvoProviderInfo.Id,
			StringComparison.OrdinalIgnoreCase);


	/// <summary>
	/// A failed request. The code says what kind of failure it was; the detail carries what the
	/// service answered (status line and start of the body), or the cause when there was no answer.
	/// </summary>
	private static JourneyResult Failed(
		DDjourneys.Core.Api.ApiException ex)
	{
		DiagnosticLog.Write(
			$"VVO request failed: {ex}");

		return JourneyResult.Failure(
			string.IsNullOrWhiteSpace(ex.Message)
				? "vvo_service_unreachable"
				: "vvo_service_error",
			ex.Detail);
	}


	private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Journey, RoutingPreferences> RoutingByJourney = [];

	private static void RememberRouting(IReadOnlyList<Journey> journeys, RoutingPreferences routing)
	{
		foreach (Journey journey in journeys)
		{
			RoutingByJourney.AddOrUpdate(journey, routing);
		}
	}

	/// <summary>The routing the journey was found with (the follow search must repeat it), or null for the default.</summary>
	private static RoutingPreferences? RoutingOf(Journey journey) =>
		RoutingByJourney.TryGetValue(journey, out RoutingPreferences? routing) ? routing : null;

	private static int GetTrackingMatchScore(
		Journey expected,
		Journey actual,
		bool strictStops = true)
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


			if (strictStops
				&& expectedLeg.Stops.Count > 0
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


	private static Location ToLocation(
		Station station) =>
		new()
		{
			Id =
				string.IsNullOrWhiteSpace(station.Id)
					? null
					: station.Id,

			ProviderId =
				string.IsNullOrWhiteSpace(station.ProviderId)
					? VvoProviderInfo.Id
					: station.ProviderId,

			Name =
				station.Name,

			Place =
				station.Place,

			Latitude =
				station.Latitude,

			Longitude =
				station.Longitude
		};


	private static VvoStandardSettings CreateStandardSettings(
		RoutingPreferences? routing = null)
	{
		routing ??= RoutingPreferences.Default;

		ModeFilter modes =
			routing.Modes == ModeFilter.None
				? ModeFilter.All
				: routing.Modes;

		return new VvoStandardSettings
		{
			MaxChanges = routing.MaxTransfers.ToString(),
			WalkingSpeed = routing.Pace.ToString(),
			FootpathToStop = Math.Clamp(routing.FootpathMinutes, 0, 30),
			IncludeAlternativeStops = routing.AlternativeStops,
			ExtraCharge =
				routing.ExtraCharge switch
				{
					ExtraChargeFilter.None => "None",
					ExtraChargeFilter.LocalTraffic => "LocalTraffic",
					_ => string.Empty
				},
			ModesOfTransport =
				[.. Enum.GetValues<ModeFilter>()
					.Where(
						mode => mode is not ModeFilter.None and not ModeFilter.All
							&& modes.HasFlag(mode))
					.Select(mode => mode.ToString())]
		};
	}


	private static VvoMobilitySettings CreateMobilitySettings(
		RoutingPreferences? routing = null)
	{
		routing ??= RoutingPreferences.Default;

		// "Individual" is the provider's profile for single switches (no stairs, no escalators,
		// fewest transfers); the preset profiles replace them.
		bool individual =
			routing.Accessibility == AccessibilityNeed.None
			&& (routing.AvoidStairs
				|| routing.AvoidEscalators
				|| routing.FewestTransfers
				|| routing.Entrance != EntranceNeed.Any);

		return new VvoMobilitySettings
		{
			MobilityRestriction =
				individual
					? "Individual"
					: routing.Accessibility.ToString(),
			SolidStairs = !routing.AvoidStairs,
			Escalators = !routing.AvoidEscalators,
			LeastChange = routing.FewestTransfers,
			Entrance = routing.Entrance.ToString()
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

		return [.. journeys.Take(maximum)];
	}
}
