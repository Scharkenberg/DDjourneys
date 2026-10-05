using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using Location = DDjourneys.Core.Models.Location;
using System.Globalization;
using System.Xml.Linq;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// The VVO TRIAS interface as a provider: journeys (with fares and a choice of optimisation),
/// stops, addresses and points of interest, and the departure monitor.
/// </summary>
public sealed class TriasProvider :
	IJourneyProvider,
	ILocationProvider,
	IDepartureProvider,
	IProviderDescriptor
{
	private readonly TriasClient _client;

	public ProviderInfo Info =>
		TriasProviderInfo.Value;

	public TriasProvider(TriasClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		_client = client;
	}

	private static TimeSpan Timeout(int seconds) =>
		TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60));

	private static bool IsOurs(Location place) =>
		string.IsNullOrWhiteSpace(place.ProviderId)
		|| string.Equals(place.ProviderId, TriasProviderInfo.Id, StringComparison.OrdinalIgnoreCase);

	private static bool IsUsable(Location place) =>
		!string.IsNullOrWhiteSpace(place.Id)
		|| (place.Latitude is not null && place.Longitude is not null);

	// ---------- Journeys ----------

	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (!IsOurs(query.From)
			|| !IsOurs(query.To)
			|| (query.Via is { } via && !IsOurs(via)))
		{
			return JourneyResult.NotSuitable("trias_endpoint_other_provider");
		}

		if (!IsUsable(query.From) || !IsUsable(query.To))
		{
			return JourneyResult.NotSuitable("trias_endpoint_missing_id");
		}

		try
		{
			XDocument response =
				await _client
					.SendAsync(
						TriasRequestKind.Trip,
						dialect => TriasRequests.Trip(query, dialect),
						cancellationToken,
						Timeout(query.TimeoutSeconds))
					.ConfigureAwait(false);

			IReadOnlyList<Journey> journeys =
				TriasMapper.MapJourneys(response, query.From, query.To);

			// The schema cannot say "no transfers" (InterchangeLimit is a positive integer).
			if (query.Routing.MaxTransfers == MaxTransfers.None)
			{
				journeys = [.. journeys.Where(journey => journey.Legs.Count <= 1)];
			}

			if (journeys.Count > 0)
			{
				return JourneyResult.Success(journeys);
			}

			if (TriasMapper.Error(response) is { } error
				&& !TriasMapper.IsNoResult(error.Code))
			{
				return JourneyResult.Failure(
					error.Code ?? "trias_error",
					error.Text);
			}

			return JourneyResult.Success([]);
		}
		catch (ApiException ex)
		{
			return JourneyResult.Failure(
				ex.IsTransient ? "trias_unreachable" : "trias_http_error",
				ex.Detail);
		}
		catch (InvalidOperationException ex)
		{
			return JourneyResult.Failure("trias_unreadable_response", ex.Message);
		}
	}

	// ---------- Places ----------

	public Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		SearchAsync(query, PlaceKinds.Stops, cancellationToken, timeout);

	public async Task<IReadOnlyList<Location>> SearchAsync(
		string query,
		PlaceKinds kinds,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		kinds |= PlaceKinds.Stops;

		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.Locations,
					dialect => TriasRequests.LocationByName(query, kinds, 12, dialect),
					cancellationToken,
					timeout)
				.ConfigureAwait(false);

		return
			[.. TriasMapper
				.MapLocations(response)
				.Where(place => kinds.HasFlag(place.Kind.ToFlag()))];
	}

	public async Task<IReadOnlyList<Location>> SearchByCoordinatesAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.Locations,
					dialect => TriasRequests.LocationByPosition(latitude, longitude, PlaceKinds.Stops, 10, dialect),
					cancellationToken,
					timeout)
				.ConfigureAwait(false);

		return
			[.. TriasMapper
				.MapLocations(response)
				.Where(place => place.Kind == PlaceKind.Stop)];
	}

	public async Task<Location?> ResolveAddressAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.Locations,
					dialect => TriasRequests.LocationByPosition(latitude, longitude, PlaceKinds.Addresses, 3, dialect),
					cancellationToken,
					timeout)
				.ConfigureAwait(false);

		return TriasMapper
			.MapLocations(response)
			.FirstOrDefault(place => place.Kind == PlaceKind.Address);
	}

	// ---------- Departures ----------

	public async Task<DepartureBoard> GetDeparturesAsync(
		DepartureQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);

		if (!IsOurs(query.Stop) || !IsUsable(query.Stop))
		{
			return DepartureBoard.Empty;
		}

		XDocument response =
			await _client
				.SendAsync(
					TriasRequestKind.StopEvent,
					dialect => TriasRequests.StopEvents(query, dialect),
					cancellationToken,
					Timeout(query.TimeoutSeconds))
				.ConfigureAwait(false);

		return TriasMapper.MapBoard(response, query.Stop, query.IsArrival);
	}

	public async Task<IReadOnlyList<RunStop>> GetRunAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(departure);

		if (departure.ProviderData is not TriasRunData run)
		{
			return [];
		}

		if (run.JourneyRef is not { } journeyRef)
		{
			return run.Stops;
		}

		// The stop event carried the run as it was then; TripInfo has the current estimates.
		string day =
			run.OperatingDayRef
			?? departure.Scheduled.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

		try
		{
			XDocument response =
				await _client
					.SendAsync(
						TriasRequestKind.TripInfo,
						dialect => TriasRequests.TripInfo(journeyRef, day, dialect),
						cancellationToken,
						Timeout(timeoutSeconds))
					.ConfigureAwait(false);

			IReadOnlyList<RunStop> stops = TriasMapper.MapRun(response, departure.StopId);

			if (stops.Count > 0)
			{
				return stops;
			}
		}
		catch (ApiException ex)
		{
			DiagnosticLog.Write($"[TRIAS] TripInfo failed, using the stop event's run: {ex.Detail}");
		}
		catch (InvalidOperationException ex)
		{
			DiagnosticLog.Write($"[TRIAS] TripInfo unreadable, using the stop event's run: {ex.Message}");
		}

		return run.Stops;
	}
}
