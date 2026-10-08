using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
		InterfaceSchemas.VvoWebApiUrl;

	private readonly ApiClient _apiClient;


	public VvoApiClient(
		ApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(
			apiClient);

		_apiClient = apiClient;
	}


	/// <summary>
	/// Searches for locations using VVO PointFinder.
	/// </summary>
	public Task<VvoPointResponse?> FindPointsAsync(
		string query,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		FindPointsAsync(
			query,
			new VvoPointFinderOptions(),
			timeout,
			cancellationToken);


	/// <summary>
	/// Searches for locations using VVO PointFinder with every documented option.
	/// </summary>
	public Task<VvoPointResponse?> FindPointsAsync(
		string query,
		VvoPointFinderOptions options,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(
			query);

		return QueryPointsAsync(
			query,
			options,
			timeout,
			cancellationToken);
	}


	/// <summary>
	/// Finds the stops around a GK4 point (PointFinder "coord:" query; stops assigned to the point come first).
	/// </summary>
	public Task<VvoPointResponse?> FindPointsByCoordinatesAsync(
		double easting,
		double northing,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		QueryPointsAsync(
			FormattableString.Invariant($"coord:{Math.Round(easting):F0}:{Math.Round(northing):F0}"),
			new VvoPointFinderOptions
			{
				Limit = 10,
				StopsOnly = false,
				AssignedStops = true
			},
			timeout,
			cancellationToken);


	private async Task<VvoPointResponse?> QueryPointsAsync(
		string query,
		VvoPointFinderOptions options,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
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
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		VvoPointResponse? response =
			JsonSerializer.Deserialize(
				json,
				VvoJsonContext.Default.VvoPointResponse);

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
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			request);

		const string requestUri =
			$"{BaseUrl}/tr/trips";

		string jsonRequest =
			JsonSerializer.Serialize(
				request,
				VvoJsonContext.Default.VvoTripRequest);

		string jsonResponse =
			await _apiClient.PostJsonAsync(
				requestUri,
				jsonRequest,
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		VvoTripResponse? response =
			JsonSerializer.Deserialize(
				jsonResponse,
				VvoJsonContext.Default.VvoTripResponse);

		EnsureProviderSuccess(
			response?.Status);

		return response;
	}


	/// <summary>
	/// Gets earlier or later connections from a VVO trip-planning session.
	/// </summary>
	public async Task<VvoTripResponse?> GetPreviousNextTripsAsync(
		VvoPrevNextRequest request,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			request);

		const string requestUri =
			$"{BaseUrl}/tr/prevnext";

		string jsonRequest =
			JsonSerializer.Serialize(
				request,
				VvoJsonContext.Default.VvoPrevNextRequest);

		string jsonResponse =
			await _apiClient.PostJsonAsync(
				requestUri,
				jsonRequest,
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		VvoTripResponse? response =
			JsonSerializer.Deserialize(
				jsonResponse,
				VvoJsonContext.Default.VvoTripResponse);

		EnsureProviderSuccess(
			response?.Status);

		return response;
	}


	/// <summary>Departure monitor: departures (or arrivals) at a stop.</summary>
	public Task<VvoDepartureResponse?> GetDeparturesAsync(
		VvoDepartureRequest request,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		PostAsync(
			"dm",
			request,
			VvoJsonContext.Default.VvoDepartureRequest,
			VvoJsonContext.Default.VvoDepartureResponse,
			timeout,
			cancellationToken);


	/// <summary>Stops of the run a departure belongs to.</summary>
	public Task<VvoRunResponse?> GetDepartureRunAsync(
		VvoDepartureRunRequest request,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		PostAsync(
			"dm/trip",
			request,
			VvoJsonContext.Default.VvoDepartureRunRequest,
			VvoJsonContext.Default.VvoRunResponse,
			timeout,
			cancellationToken);


	/// <summary>One way of asking dm/trip; the wire format of "time" is what differs.</summary>
	public sealed record VvoRunAttempt(string Label, HttpMethod Method, string Uri, string? Body);

	// The attempt that worked last time goes first next time.
	private string? _runLabel;

	private static readonly Lazy<TimeZoneInfo> VvoZone =
		new(
			() =>
			{
				foreach (string id in new[] { "Europe/Berlin", "W. Europe Standard Time" })
				{
					try
					{
						return TimeZoneInfo.FindSystemTimeZoneById(id);
					}
					catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
					{
					}
				}

				return TimeZoneInfo.Local;
			});

	/// <summary>"/Date(ms+0200)/" with the offset of the provider zone.</summary>
	public static string ToVvoDate(
		DateTimeOffset value,
		bool providerOffset = true)
	{
		TimeSpan offset =
			providerOffset
				? VvoZone.Value.GetUtcOffset(value)
				: TimeSpan.Zero;

		return string.Create(
			System.Globalization.CultureInfo.InvariantCulture,
			$"/Date({value.ToUnixTimeMilliseconds()}{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):00}{Math.Abs(offset.Minutes):00})/");
	}

	/// <summary>
	/// The ways to ask for the course of a run, most promising first:
	/// the GET form the dvb-fahrplan app uses (UTC milliseconds, "-0000"), then the documented POST with the
	/// date as the server itself writes it (escaped slashes), as ISO 8601, and as plain "/Date()/".
	/// </summary>
	public IReadOnlyList<VvoRunAttempt> BuildRunAttempts(
		string tripId,
		string stopId,
		bool isArrival,
		DateTimeOffset time)
	{
		long ms = time.ToUnixTimeMilliseconds();

		string Json(string timeJson) =>
			"{\"tripid\":" + JsonSerializer.Serialize(tripId, VvoJsonContext.Default.String)
			+ ",\"time\":" + timeJson
			+ ",\"stopid\":" + JsonSerializer.Serialize(stopId, VvoJsonContext.Default.String)
			+ ",\"isarrival\":" + (isArrival ? "true" : "false")
			+ ",\"mapdata\":false,\"format\":\"json\"}";

		string get =
			$"{BaseUrl}/dm/trip?format=json"
			+ $"&time={Uri.EscapeDataString($"/Date({ms}-0000)/")}"
			+ $"&tripId={Uri.EscapeDataString(tripId)}"
			+ $"&stopId={Uri.EscapeDataString(stopId)}"
			+ $"&isarrival={(isArrival ? "true" : "false")}";

		string post = $"{BaseUrl}/dm/trip";

		VvoRunAttempt[] attempts =
		[
			new("get-utc", HttpMethod.Get, get, null),
			new("post-escaped", HttpMethod.Post, post, Json($"\"\\/Date({ms}+0000)\\/\"")),
			new("post-iso", HttpMethod.Post, post, Json(JsonSerializer.Serialize(time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture), VvoJsonContext.Default.String))),
			new("post-plain", HttpMethod.Post, post, Json(JsonSerializer.Serialize(ToVvoDate(time), VvoJsonContext.Default.String)))
		];

		return
			[.. attempts
				.OrderBy(attempt => attempt.Label == _runLabel ? 0 : 1)];
	}

	/// <summary>
	/// Tries the attempts until one returns a run that <paramref name="accept"/> takes, logging every
	/// request and what came back.
	/// </summary>
	public async Task<VvoRunResponse?> GetDepartureRunAsync(
		IReadOnlyList<VvoRunAttempt> attempts,
		Func<VvoRunResponse, bool> accept,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(attempts);
		ArgumentNullException.ThrowIfNull(accept);

		foreach (VvoRunAttempt attempt in attempts)
		{
			cancellationToken.ThrowIfCancellationRequested();

			DiagnosticLog.Write(
				$"[VVO run] try '{attempt.Label}': {attempt.Method} {attempt.Uri} {attempt.Body}");

			try
			{
				string json =
					attempt.Method == HttpMethod.Get
						? await _apiClient.GetAsync(attempt.Uri, timeout, cancellationToken).ConfigureAwait(false)
						: await _apiClient.PostJsonAsync(attempt.Uri, attempt.Body ?? "{}", timeout, cancellationToken).ConfigureAwait(false);

				VvoRunResponse? response =
					JsonSerializer.Deserialize(json, VvoJsonContext.Default.VvoRunResponse);

				if (response is null)
				{
					DiagnosticLog.Write($"[VVO run] '{attempt.Label}': empty answer: {Head(json)}");

					continue;
				}

				EnsureProviderSuccess(response.Status);

				DiagnosticLog.Write(
					$"[VVO run] '{attempt.Label}': OK, {response.Stops.Count} stops, status {response.Status?.Code}");

				if (accept(response))
				{
					_runLabel = attempt.Label;

					return response;
				}

				DiagnosticLog.Write($"[VVO run] '{attempt.Label}': answered, but not the run we asked for");
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex) when (ex is ApiException or InvalidOperationException)
			{
				string body =
					ex is ApiException api
						? api.ResponseBody ?? string.Empty
						: ex.Message;

				DiagnosticLog.Write(
					$"[VVO run] '{attempt.Label}': failed, status {(ex as ApiException)?.StatusCode?.ToString() ?? "none"}: {Head(body)}");
			}
		}

		return null;
	}

	private static string Head(
		string text)
	{
		string flat = text.ReplaceLineEndings(" ");

		return flat[..Math.Min(flat.Length, 400)];
	}


	/// <summary>The connection with one leg replaced by an earlier or later alternative.</summary>
	public Task<VvoTripResponse?> GetLegAlternativeAsync(
		VvoPrevNextMoveRequest request,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		PostAsync(
			"tr/prevnextmove",
			request,
			VvoJsonContext.Default.VvoPrevNextMoveRequest,
			VvoJsonContext.Default.VvoTripResponse,
			timeout,
			cancellationToken);


	/// <summary>Route changes and network notices.</summary>
	public Task<VvoRouteChangesResponse?> GetRouteChangesAsync(
		VvoRouteChangesRequest request,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		PostAsync(
			"rc",
			request,
			VvoJsonContext.Default.VvoRouteChangesRequest,
			VvoJsonContext.Default.VvoRouteChangesResponse,
			timeout,
			cancellationToken);


	/// <summary>Lines that currently have a route change.</summary>
	public Task<VvoChangedLinesResponse?> GetChangedLinesAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		PostAsync(
			"rc/lines",
			new VvoChangedLinesRequest(),
			VvoJsonContext.Default.VvoChangedLinesRequest,
			VvoJsonContext.Default.VvoChangedLinesResponse,
			timeout,
			cancellationToken);


	/// <summary>Lines that serve a stop.</summary>
	public Task<VvoStopLinesResponse?> GetStopLinesAsync(
		string stopId,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(
			stopId);

		return PostAsync(
			"stt/lines",
			new VvoStopLinesRequest { StopId = stopId },
			VvoJsonContext.Default.VvoStopLinesRequest,
			VvoJsonContext.Default.VvoStopLinesResponse,
			timeout,
			cancellationToken);
	}


	/// <summary>Map markers inside a box of GK4 coordinates.</summary>
	public Task<VvoMapPinsResponse?> GetMapPinsAsync(
		VvoMapPinsRequest request,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		PostAsync(
			"map/pins",
			request,
			VvoJsonContext.Default.VvoMapPinsRequest,
			VvoJsonContext.Default.VvoMapPinsResponse,
			timeout,
			cancellationToken);


	/// <summary>The polygons of the tariff zones.</summary>
	public Task<VvoMapPolygonsResponse?> GetTariffPolygonsAsync(
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		PostAsync(
			"map/polygons",
			new VvoMapPolygonsRequest(),
			VvoJsonContext.Default.VvoMapPolygonsRequest,
			VvoJsonContext.Default.VvoMapPolygonsResponse,
			timeout,
			cancellationToken);


	/// <summary>One way of asking for the PDF: a label for the log and the address.</summary>
	public sealed record VvoPdfAttempt(string Label, Uri Uri);


	/// <summary>Address of the PDF of a planned trip (the first, most complete attempt).</summary>
	public static Uri BuildTripPdfUri(
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
	public static IReadOnlyList<VvoPdfAttempt> BuildTripPdfAttempts(
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

		parameters.Add(("mobilitysettings", JsonSerializer.Serialize(mobilitySettings, VvoJsonContext.Default.VvoMobilitySettings)));
		parameters.Add(("standardSettings", JsonSerializer.Serialize(standardSettings, VvoJsonContext.Default.VvoStandardSettings)));
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
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
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
							timeout,
							cancellationToken)
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
		JsonTypeInfo<TRequest> requestInfo,
		JsonTypeInfo<TResponse> responseInfo,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
		where TResponse : class
	{
		ArgumentNullException.ThrowIfNull(
			request);

		string jsonResponse =
			await _apiClient.PostJsonAsync(
				$"{BaseUrl}/{path}",
				JsonSerializer.Serialize(
					request,
					requestInfo),
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		TResponse? response =
			JsonSerializer.Deserialize(
				jsonResponse,
				responseInfo);

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