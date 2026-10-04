using System.Text.Json;
using DDjourneys.Core.Api;
using DDjourneys.Core.Providers.Vvo.Extensions;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Requests;
using DDjourneys.Core.Providers.Vvo.Serialization;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>
/// Provides access to the VVO WebAPI.
/// </summary>
public sealed class VvoApiClient
{
	private const string BaseUrl =
		"https://webapi.vvo-online.de";

	private readonly ApiClient _apiClient;
	private readonly JsonSerializerOptions _jsonOptions;


	public VvoApiClient(
		ApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(
			apiClient);

		_apiClient = apiClient;

		_jsonOptions =
			new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,

				DefaultIgnoreCondition =
					System.Text.Json.Serialization
						.JsonIgnoreCondition
						.WhenWritingNull
			};

		_jsonOptions.Converters.Add(
			new VvoDateTimeOffsetConverter());
	}


	/// <summary>
	/// Searches for locations using VVO PointFinder.
	/// </summary>
	public Task<VvoPointResponse?> FindPointsAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		FindPointsAsync(
			query,
			new VvoPointFinderOptions(),
			cancellationToken,
			timeout);


	/// <summary>
	/// Searches for locations using VVO PointFinder with every documented option.
	/// </summary>
	public Task<VvoPointResponse?> FindPointsAsync(
		string query,
		VvoPointFinderOptions options,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(
			query);

		return QueryPointsAsync(
			query,
			options,
			cancellationToken,
			timeout);
	}


	/// <summary>
	/// Finds the stops around a GK4 point (PointFinder "coord:" query; stops assigned to the point come first).
	/// </summary>
	public Task<VvoPointResponse?> FindPointsByCoordinatesAsync(
		double easting,
		double northing,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		QueryPointsAsync(
			FormattableString.Invariant($"coord:{Math.Round(easting):F0}:{Math.Round(northing):F0}"),
			new VvoPointFinderOptions
			{
				Limit = 10,
				StopsOnly = false,
				AssignedStops = true
			},
			cancellationToken,
			timeout);


	private async Task<VvoPointResponse?> QueryPointsAsync(
		string query,
		VvoPointFinderOptions options,
		CancellationToken cancellationToken,
		TimeSpan? timeout)
	{
		string requestUri =
			$"{BaseUrl}/tr/pointfinder" +
			$"?query={Uri.EscapeDataString(query)}" +
			$"&limit={Math.Clamp(options.Limit, 1, 100)}" +
			$"&stopsOnly={Flag(options.StopsOnly)}" +
			(options.RegionalOnly ? "&regionalOnly=true" : string.Empty) +
			(options.StopShortcuts ? "&stopShortcuts=true" : string.Empty) +
			(options.AssignedStops ? "&assignedstops=true" : string.Empty) +
			(options.ShowLines ? "&showlines=true" : string.Empty) +
			"&format=json";

		string json =
			await _apiClient.GetAsync(
				requestUri,
				cancellationToken,
				timeout)
				.ConfigureAwait(false);

		VvoPointResponse? response =
			JsonSerializer.Deserialize<VvoPointResponse>(
				json,
				_jsonOptions);

		EnsureProviderSuccess(
			response?.Status);

		return response;
	}


	private static string Flag(
		bool value) =>
		value ? "true" : "false";


	/// <summary>
	/// Searches for journeys using the VVO trip planner.
	/// </summary>
	public async Task<VvoTripResponse?> GetTripsAsync(
		VvoTripRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(
			request);

		const string requestUri =
			$"{BaseUrl}/tr/trips";

		string jsonRequest =
			JsonSerializer.Serialize(
				request,
				_jsonOptions);

		string jsonResponse =
			await _apiClient.PostJsonAsync(
				requestUri,
				jsonRequest,
				cancellationToken,
				timeout)
				.ConfigureAwait(false);

		VvoTripResponse? response =
			JsonSerializer.Deserialize<VvoTripResponse>(
				jsonResponse,
				_jsonOptions);

		System.Diagnostics.Debug.WriteLine(jsonResponse);

		EnsureProviderSuccess(
			response?.Status);

		return response;
	}


	/// <summary>
	/// Gets earlier or later connections from a VVO trip-planning session.
	/// </summary>
	public async Task<VvoTripResponse?> GetPreviousNextTripsAsync(
		VvoPrevNextRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(
			request);

		const string requestUri =
			$"{BaseUrl}/tr/prevnext";

		string jsonRequest =
			JsonSerializer.Serialize(
				request,
				_jsonOptions);

		string jsonResponse =
			await _apiClient.PostJsonAsync(
				requestUri,
				jsonRequest,
				cancellationToken,
				timeout)
				.ConfigureAwait(false);

		VvoTripResponse? response =
			JsonSerializer.Deserialize<VvoTripResponse>(
				jsonResponse,
				_jsonOptions);

		EnsureProviderSuccess(
			response?.Status);

		return response;
	}


	/// <summary>Departure monitor: departures (or arrivals) at a stop.</summary>
	public Task<VvoDepartureResponse?> GetDeparturesAsync(
		VvoDepartureRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		PostAsync<VvoDepartureRequest, VvoDepartureResponse>(
			"dm",
			request,
			cancellationToken,
			timeout);


	/// <summary>Stops of the run a departure belongs to.</summary>
	public Task<VvoRunResponse?> GetDepartureRunAsync(
		VvoDepartureRunRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		PostAsync<VvoDepartureRunRequest, VvoRunResponse>(
			"dm/trip",
			request,
			cancellationToken,
			timeout);


	/// <summary>The connection with one leg replaced by an earlier or later alternative.</summary>
	public Task<VvoTripResponse?> GetLegAlternativeAsync(
		VvoPrevNextMoveRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		PostAsync<VvoPrevNextMoveRequest, VvoTripResponse>(
			"tr/prevnextmove",
			request,
			cancellationToken,
			timeout);


	/// <summary>Route changes and network notices.</summary>
	public Task<VvoRouteChangesResponse?> GetRouteChangesAsync(
		VvoRouteChangesRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		PostAsync<VvoRouteChangesRequest, VvoRouteChangesResponse>(
			"rc",
			request,
			cancellationToken,
			timeout);


	/// <summary>Lines that currently have a route change.</summary>
	public Task<VvoChangedLinesResponse?> GetChangedLinesAsync(
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		PostAsync<VvoChangedLinesRequest, VvoChangedLinesResponse>(
			"rc/lines",
			new VvoChangedLinesRequest(),
			cancellationToken,
			timeout);


	/// <summary>Lines that serve a stop.</summary>
	public Task<VvoStopLinesResponse?> GetStopLinesAsync(
		string stopId,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(
			stopId);

		return PostAsync<VvoStopLinesRequest, VvoStopLinesResponse>(
			"stt/lines",
			new VvoStopLinesRequest { StopId = stopId },
			cancellationToken,
			timeout);
	}


	/// <summary>Map markers inside a box of GK4 coordinates.</summary>
	public Task<VvoMapPinsResponse?> GetMapPinsAsync(
		VvoMapPinsRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		PostAsync<VvoMapPinsRequest, VvoMapPinsResponse>(
			"map/pins",
			request,
			cancellationToken,
			timeout);


	/// <summary>The polygons of the tariff zones.</summary>
	public Task<VvoMapPolygonsResponse?> GetTariffPolygonsAsync(
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null) =>
		PostAsync<VvoMapPolygonsRequest, VvoMapPolygonsResponse>(
			"map/polygons",
			new VvoMapPolygonsRequest(),
			cancellationToken,
			timeout);


	/// <summary>
	/// Address of the PDF of a planned trip (GET tr/trippdf). The caller opens it; nothing is downloaded here.
	/// </summary>
	public Uri BuildTripPdfUri(
		string routeId,
		string sessionId,
		string origin,
		string destination,
		DateTimeOffset time,
		bool isArrivalTime,
		string? via,
		VvoStandardSettings standardSettings,
		VvoMobilitySettings mobilitySettings)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(routeId);
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		ArgumentException.ThrowIfNullOrWhiteSpace(origin);
		ArgumentException.ThrowIfNullOrWhiteSpace(destination);
		ArgumentNullException.ThrowIfNull(standardSettings);
		ArgumentNullException.ThrowIfNull(mobilitySettings);

		static string Escape(string value) =>
			Uri.EscapeDataString(value);

		string query =
			$"id={Escape(routeId)}" +
			$"&origin={Escape(origin)}" +
			$"&destination={Escape(destination)}" +
			$"&sessionid={Escape(sessionId)}" +
			$"&time={Escape(time.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}" +
			$"&isarrivaltime={Flag(isArrivalTime)}" +
			(string.IsNullOrWhiteSpace(via) ? string.Empty : $"&via={Escape(via)}") +
			$"&mobilitysettings={Escape(JsonSerializer.Serialize(mobilitySettings, _jsonOptions))}" +
			$"&standardSettings={Escape(JsonSerializer.Serialize(standardSettings, _jsonOptions))}" +
			"&numberprev=0&numbernext=0";

		return new Uri($"{BaseUrl}/tr/trippdf?{query}");
	}


	private async Task<TResponse?> PostAsync<TRequest, TResponse>(
		string path,
		TRequest request,
		CancellationToken cancellationToken,
		TimeSpan? timeout)
		where TResponse : class
	{
		ArgumentNullException.ThrowIfNull(
			request);

		string jsonResponse =
			await _apiClient.PostJsonAsync(
				$"{BaseUrl}/{path}",
				JsonSerializer.Serialize(
					request,
					_jsonOptions),
				cancellationToken,
				timeout)
				.ConfigureAwait(false);

		TResponse? response =
			JsonSerializer.Deserialize<TResponse>(
				jsonResponse,
				_jsonOptions);

		EnsureProviderSuccess(
			StatusOf(response));

		return response;
	}


	private static VvoStatus? StatusOf(
		object? response) =>
		response switch
		{
			VvoDepartureResponse r => r.Status,
			VvoRunResponse r => r.Status,
			VvoTripResponse r => r.Status,
			VvoRouteChangesResponse r => r.Status,
			VvoChangedLinesResponse r => r.Status,
			VvoStopLinesResponse r => r.Status,
			VvoMapPinsResponse r => r.Status,
			VvoMapPolygonsResponse r => r.Status,
			_ => null
		};


	private static void EnsureProviderSuccess(
		VvoStatus? status)
	{
		if (status is null
			|| status.IsSuccess()
			|| status.IsNoData())
		{
			return;
		}

		string? message =
			status.Message;

		if (string.IsNullOrWhiteSpace(message))
		{
			message = null;
		}

		bool transient =
			status.IsServerError()
			|| status.Code?.Contains(
				"Server",
				StringComparison.OrdinalIgnoreCase)
				== true;

		throw new ApiException(
			message ?? string.Empty,
			isTransient: transient);
	}
}