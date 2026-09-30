namespace DDjourneys.Core.Api;

/// <summary>Small HTTP client with bounded retries for idempotent GET requests.</summary>
public sealed class ApiClient : IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly bool _ownsClient;
	private const int MaxRetries = 3;

	/// <summary>Budget used when the caller passes none. The HttpClient itself has no timeout of its own,
	/// otherwise it would cap every per-call timeout longer than its fixed value.</summary>
	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

	public ApiClient()
	{
		_httpClient = CreateHttpClient();
		_ownsClient = true;
	}

	public ApiClient(HttpClient httpClient)
	{
		ArgumentNullException.ThrowIfNull(httpClient);
		_httpClient = httpClient;
		_ownsClient = false;
	}

	/// <summary>Sends a GET request with bounded retries for transient failures.</summary>
	public async Task<string> GetAsync(string requestUri, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
	{
		using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
		CancellationToken requestToken = linkedCts.Token;
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, requestToken).ConfigureAwait(false);
				string content = await response.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
				EnsureSuccess(response, content);
				return content;
			}
			catch (ApiException ex) when (ex.IsTransient && attempt < MaxRetries)
			{
				await DelayRetryAsync(ex.RetryAfter, attempt, requestToken).ConfigureAwait(false);
			}
			catch (HttpRequestException) when (attempt < MaxRetries)
			{
				await DelayRetryAsync(null, attempt, requestToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				throw new ApiException("HTTP request timed out.");
			}
			catch (HttpRequestException ex)
			{
				throw new ApiException("Network error while contacting API.", null, true, null, ex);
			}
		}
	}

	/// <summary>
	/// Sends a JSON POST once. A dropped response does not prove the server did not process
	/// the request, so replaying it could create duplicate work at the provider.
	/// </summary>
	public async Task<string> PostJsonAsync(string requestUri, string json, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
	{
		try
		{
			using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
			CancellationToken requestToken = linkedCts.Token;
			using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await _httpClient.PostAsync(requestUri, content, requestToken).ConfigureAwait(false);
			string responseContent = await response.Content.ReadAsStringAsync(requestToken).ConfigureAwait(false);
			EnsureSuccess(response, responseContent);
			return responseContent;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ApiException("HTTP request timed out.");
		}
		catch (HttpRequestException ex)
		{
			throw new ApiException("Network error while contacting API.", null, true, null, ex);
		}
	}

	private static async Task DelayRetryAsync(TimeSpan? retryAfter, int attempt, CancellationToken cancellationToken)
	{
		TimeSpan delay = retryAfter ?? attempt switch
		{
			1 => TimeSpan.FromMilliseconds(500),
			2 => TimeSpan.FromSeconds(2),
			_ => TimeSpan.FromSeconds(4)
		};
		// A broken server can return an arbitrarily long Retry-After value.
		if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromSeconds(30)) delay = TimeSpan.FromSeconds(30);
		await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
	}

	private static void EnsureSuccess(HttpResponseMessage response, string responseBody)
	{
		if (response.IsSuccessStatusCode) return;

		int statusCode = (int)response.StatusCode;
		bool transient = statusCode is 408 or 425 or 429 or 500 or 502 or 503 or 504;
		string message = statusCode switch
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

		throw new ApiException(message, statusCode, transient, responseBody, retryAfter: response.Headers.RetryAfter?.Delta);
	}

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		client.DefaultRequestHeaders.UserAgent.ParseAdd("DDjourneys/0.2");
		return client;
	}

	public void Dispose()
	{
		if (_ownsClient) _httpClient.Dispose();
	}
}
