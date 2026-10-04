using System.Text.Json;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;
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


	/// <summary>One way of asking for the PDF: a label for the log and the address.</summary>
	public sealed record VvoPdfAttempt(string Label, Uri Uri);


	/// <summary>Address of the PDF of a planned trip (the first, most complete attempt).</summary>
	public Uri BuildTripPdfUri(
		string routeId,
		string sessionId,
		string origin,
		string destination,
		DateTimeOffset time,
		bool isArrivalTime,
		string? via,
		VvoStandardSettings standardSettings,
		VvoMobilitySettings mobilitySettings) =>
		BuildTripPdfAttempts(
			routeId,
			sessionId,
			origin,
			destination,
			time,
			isArrivalTime,
			via,
			standardSettings,
			mobilitySettings)[0].Uri;


	/// <summary>
	/// The ways to ask for the PDF (GET tr/trippdf), from the full request down to the exact shape of the
	/// documented example: first everything, then without the standard settings, without the mobility
	/// settings, without via, and finally with the session id's colon unescaped. Identical addresses are
	/// listed once.
	/// </summary>
	public IReadOnlyList<VvoPdfAttempt> BuildTripPdfAttempts(
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

		var parameters =
			new List<(string Key, string Value)>
			{
				("id", routeId),
				("origin", origin),
				("destination", destination),
				("sessionid", sessionId),
				("time", time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture)),
				("isarrivaltime", Flag(isArrivalTime))
			};

		if (!string.IsNullOrWhiteSpace(via))
		{
			parameters.Add(("via", via));
		}

		parameters.Add(("mobilitysettings", JsonSerializer.Serialize(mobilitySettings, _jsonOptions)));
		parameters.Add(("standardSettings", JsonSerializer.Serialize(standardSettings, _jsonOptions)));
		parameters.Add(("numberprev", "0"));
		parameters.Add(("numbernext", "0"));
		parameters.Add(("format", "json"));

		static string Query(IEnumerable<(string Key, string Value)> items, bool rawColon) =>
			string.Join(
				"&",
				items.Select(
					item =>
					{
						string escaped = Uri.EscapeDataString(item.Value);

						return $"{item.Key}={(rawColon && item.Key == "sessionid" ? escaped.Replace("%3A", ":") : escaped)}";
					}));

		var steps = new List<(string Label, string Query)>();

		IEnumerable<(string Key, string Value)> Without(params string[] keys) =>
			parameters.Where(
				item => !keys.Contains(item.Key, StringComparer.OrdinalIgnoreCase));

		steps.Add(("full", Query(parameters, false)));
		steps.Add(("without standardSettings", Query(Without("standardSettings"), false)));
		steps.Add(("without standardSettings, mobilitysettings", Query(Without("standardSettings", "mobilitysettings"), false)));
		steps.Add(("documented shape (also without via)", Query(Without("standardSettings", "mobilitysettings", "via"), false)));
		steps.Add(("documented shape, raw colon in sessionid", Query(Without("standardSettings", "mobilitysettings", "via"), true)));

		return
			[.. steps
				.DistinctBy(step => step.Query)
				.Select(step => new VvoPdfAttempt(step.Label, new Uri($"{BaseUrl}/tr/trippdf?{step.Query}")))];
	}


	/// <summary>
	/// Tries the attempts in order and returns the first answer that is a PDF; null when none is.
	/// Every attempt (parameters, status, media type, size, start of a non-PDF body) goes to the diagnostic log.
	/// </summary>
	public async Task<byte[]?> DownloadTripPdfAsync(
		IReadOnlyList<VvoPdfAttempt> attempts,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(attempts);

		foreach (VvoPdfAttempt attempt in attempts)
		{
			cancellationToken.ThrowIfCancellationRequested();

			DiagnosticLog.Write($"[VVO PDF] try '{attempt.Label}': {attempt.Uri}");

			try
			{
				(byte[] content, string? mediaType) =
					await _apiClient
						.GetBytesAsync(
							attempt.Uri.AbsoluteUri,
							"application/pdf, */*",
							cancellationToken,
							timeout)
						.ConfigureAwait(false);

				bool isPdf =
					content.Length >= 4
					&& content[0] == 0x25
					&& content[1] == 0x50
					&& content[2] == 0x44
					&& content[3] == 0x46;

				if (isPdf)
				{
					DiagnosticLog.Write($"[VVO PDF] '{attempt.Label}': OK, {content.Length} bytes, {mediaType}");

					return content;
				}

				string start =
					System.Text.Encoding.UTF8
						.GetString(content, 0, Math.Min(content.Length, 300))
						.ReplaceLineEndings(" ");

				DiagnosticLog.Write($"[VVO PDF] '{attempt.Label}': 200 but not a PDF ({mediaType}, {content.Length} bytes): {start}");
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (ApiException ex)
			{
				string body =
					(ex.ResponseBody ?? string.Empty)
						.ReplaceLineEndings(" ");

				DiagnosticLog.Write(
					$"[VVO PDF] '{attempt.Label}': failed, status {ex.StatusCode?.ToString() ?? "none"}: {body[..Math.Min(body.Length, 300)]} {ex.Detail}");
			}
		}

		return null;
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