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

	public VvoApiClient(ApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(apiClient);

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
	public async Task<VvoPointResponse?> FindPointsAsync(
		string query,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(query);

		string requestUri =
			$"{BaseUrl}/tr/pointfinder" +
			$"?query={Uri.EscapeDataString(query)}" +
			"&stopsOnly=true" +
			"&limit=30" +
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

	/// <summary>
	/// Searches for journeys using the VVO trip planner.
	/// </summary>
	public async Task<VvoTripResponse?> GetTripsAsync(
		VvoTripRequest request,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(request);

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

		EnsureProviderSuccess(
			response?.Status);

		return response;
	}

	private static void EnsureProviderSuccess(
		VvoStatus? status)
	{
		if (status is null
			|| status.IsSuccess()
			|| status.IsNoData())
		{
			return;
		}

		string? message = status.Message;

		if (string.IsNullOrWhiteSpace(message))
		{
			message = null;
		}

		bool transient =
			status.IsServerError()
			|| status.Code?.Contains(
				"Server",
				StringComparison.OrdinalIgnoreCase) == true;

		throw new ApiException(
			message ?? string.Empty,
			isTransient: transient);
	}
}