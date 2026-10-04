using DDjourneys.Core.Api;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using Location = DDjourneys.Core.Models.Location;
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
						TriasRequests.Trip(query),
						cancellationToken,
						Timeout(query.TimeoutSeconds))
					.ConfigureAwait(false);

			IReadOnlyList<Journey> journeys =
				TriasMapper.MapJourneys(response, query.From, query.To);

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
					TriasRequests.LocationByName(query, kinds, 12),
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
					TriasRequests.LocationByPosition(latitude, longitude, PlaceKinds.Stops, 10),
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
					TriasRequests.LocationByPosition(latitude, longitude, PlaceKinds.Addresses, 3),
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
					TriasRequests.StopEvents(query),
					cancellationToken,
					Timeout(query.TimeoutSeconds))
				.ConfigureAwait(false);

		return TriasMapper.MapBoard(response, query.Stop, query.IsArrival);
	}

	public Task<IReadOnlyList<RunStop>> GetRunAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(departure);

		// The stop event already carried the whole run.
		return Task.FromResult<IReadOnlyList<RunStop>>(
			departure.ProviderData is TriasRunData run
				? run.Stops
				: []);
	}
}
