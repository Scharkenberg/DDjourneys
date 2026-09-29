namespace DDjourneys.Core.Api;

/// <summary>
/// Provides basic HTTP communication functionality.
/// </summary>
public sealed class ApiClient : IDisposable
{
	private readonly HttpClient _httpClient;

	private readonly bool _ownsClient;

	private const int MaxRetries = 3;

	/// <summary>
	/// Creates an API client using its own HttpClient instance.
	/// </summary>
	public ApiClient()
	{
		_httpClient = CreateHttpClient();
		_ownsClient = true;
	}


	/// <summary>
	/// Creates an API client using an externally managed HttpClient.
	/// </summary>
	public ApiClient(HttpClient httpClient)
	{
		ArgumentNullException.ThrowIfNull(httpClient);

		_httpClient = httpClient;
		_ownsClient = false;
	}


	/// <summary>
	/// Sends a GET request and returns the response body.
	/// </summary>
	public async Task<string> GetAsync(
		string requestUri,
		CancellationToken cancellationToken = default)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				using HttpResponseMessage response =
					await _httpClient.GetAsync(
						requestUri,
						cancellationToken)
					.ConfigureAwait(false);


				string content =
					await response.Content
						.ReadAsStringAsync(cancellationToken)
						.ConfigureAwait(false);


				await EnsureSuccessAsync(response, content,	cancellationToken);

				return content;
			}
			catch (ApiException ex)	when (ex.IsTransient && attempt < MaxRetries)
			{
				await DelayRetryAsync(
					ex.RetryAfter,
					attempt,
					cancellationToken);
			}
			catch (HttpRequestException ex)
				when (attempt < MaxRetries)
			{
				await DelayRetryAsync(
					null,
					attempt,
					cancellationToken);
			}
			catch (OperationCanceledException)
				when (!cancellationToken.IsCancellationRequested)
			{
				throw new ApiException(
					"HTTP request timed out.");
			}
			catch (HttpRequestException ex)
			{
				throw new ApiException("Network error while contacting API.", null, true, null, ex);
			}
		}
	}


	/// <summary>
	/// Sends a POST request containing JSON and returns the response body.
	/// </summary>
	public async Task<string> PostJsonAsync(
	string requestUri,
	string json,
	CancellationToken cancellationToken = default)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				using var content = new StringContent(
					json,
					System.Text.Encoding.UTF8,
					"application/json");


				using HttpResponseMessage response =
					await _httpClient.PostAsync(
						requestUri,
						content,
						cancellationToken)
					.ConfigureAwait(false);


				string responseContent =
					await response.Content
						.ReadAsStringAsync(cancellationToken)
						.ConfigureAwait(false);


				await EnsureSuccessAsync(response, responseContent, cancellationToken);

				return responseContent;
			}
			catch (ApiException ex)	when (ex.IsTransient && attempt < MaxRetries)
			{
				await DelayRetryAsync(
					ex.RetryAfter,
					attempt,
					cancellationToken);
			}
			catch (HttpRequestException ex)
				when (attempt < MaxRetries)
			{
				await DelayRetryAsync(
					null,
					attempt,
					cancellationToken);
			}
			catch (OperationCanceledException)
				when (!cancellationToken.IsCancellationRequested)
			{
				throw new ApiException(
					"HTTP request timed out.");
			}
			catch (HttpRequestException ex)
			{
				throw new ApiException("Network error while contacting API.", null,	true, null,	ex);
			}
		}
	}

	private static bool ShouldRetry(
	Exception exception)
	{
		return exception switch
		{
			ApiException apiException
				when apiException.IsTransient =>
				true,

			HttpRequestException =>
				true,

			_ =>
				false
		};
	}

	private static async Task DelayRetryAsync(
	TimeSpan? retryAfter,
	int attempt,
	CancellationToken cancellationToken)
	{
		TimeSpan delay =
			retryAfter ??
			GetDefaultRetryDelay(attempt);


		await Task.Delay(
			delay,
			cancellationToken);
	}

	private static TimeSpan GetDefaultRetryDelay(
	int attempt)
	{
		return attempt switch
		{
			1 => TimeSpan.FromMilliseconds(500),
			2 => TimeSpan.FromSeconds(2),
			_ => TimeSpan.FromSeconds(4)
		};
	}

	private static async Task EnsureSuccessAsync(
	HttpResponseMessage response,
	string responseBody,
	CancellationToken cancellationToken)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}


		int statusCode =
			(int)response.StatusCode;


		bool transient =
			statusCode switch
			{
				408 => true, // Request Timeout
				425 => true, // Too Early
				429 => true, // Too Many Requests
				500 => true, // Internal Server Error
				502 => true, // Bad Gateway
				503 => true, // Service Unavailable
				504 => true, // Gateway Timeout
				_ => false
			};


		string message =
			statusCode switch
			{
				400 => "The API rejected the request.",
				401 => "The API requires authentication.",
				403 => "The API denied access.",
				404 => "The requested API endpoint was not found.",
				408 => "The API request timed out.",
				429 => "The API rate limit was exceeded.",
				500 => "The API returned an internal server error.",
				502 => "The API gateway failed.",
				503 => "The API service is unavailable.",
				504 => "The API gateway timed out.",
				_ => $"The API returned HTTP {statusCode}."
			};


		throw new ApiException(
			message,
			statusCode,
			transient,
			responseBody,
			null,
			retryAfter: response.Headers.RetryAfter?.Delta);
	}


	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(15)
		};

		client.DefaultRequestHeaders.Accept.ParseAdd(
			"application/json");

		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"curl/8.22.0");

		return client;
	}


	public void Dispose()
	{
		if (_ownsClient)
		{
			_httpClient.Dispose();
		}
	}
}