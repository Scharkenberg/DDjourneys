namespace DDjourneys.Core.Api;

/// <summary>Small HTTP client with bounded retries for idempotent GET requests.</summary>
public sealed class ApiClient : IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly bool _ownsClient;
	private const int MaxRetries = 3;

	/// <summary>
	/// Budget used when the caller passes none. The HttpClient itself has no timeout of its own,
	/// otherwise it would cap every per-call timeout longer than its fixed value.
	/// </summary>
	private static readonly TimeSpan DefaultTimeout =
		TimeSpan.FromSeconds(15);

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
	public async Task<string> GetAsync(
		string requestUri,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		using var timeoutCts =
			new CancellationTokenSource(
				timeout ?? DefaultTimeout);

		using var linkedCts =
			CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				timeoutCts.Token);

		CancellationToken requestToken =
			linkedCts.Token;

		for (int attempt = 1; ; attempt++)
		{
			try
			{
				using HttpResponseMessage response =
					await _httpClient
						.GetAsync(
							requestUri,
							requestToken)
						.ConfigureAwait(false);

				string content =
					await response.Content
						.ReadAsStringAsync(requestToken)
						.ConfigureAwait(false);

				EnsureSuccess(
					response,
					content);

				return content;
			}
			catch (ApiException ex)
				when (ex.IsTransient && attempt < MaxRetries)
			{
				await DelayRetryAsync(
					ex.RetryAfter,
					attempt,
					requestToken)
					.ConfigureAwait(false);
			}
			catch (HttpRequestException)
				when (attempt < MaxRetries)
			{
				await DelayRetryAsync(
					null,
					attempt,
					requestToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException ex)
				when (!cancellationToken.IsCancellationRequested)
			{
				// Our own time budget ran out. No message (callers show localized text), the cause stays for logs.
				throw new ApiException(
					string.Empty,
					null,
					true,
					null,
					ex);
			}
			catch (HttpRequestException ex)
			{
				throw new ApiException(
					string.Empty,
					null,
					true,
					null,
					ex);
			}
		}
	}

	/// <summary>
	/// Sends a JSON POST once. A dropped response does not prove the server did not process
	/// the request, so replaying it could create duplicate work at the provider.
	/// </summary>
	public async Task<string> PostJsonAsync(
		string requestUri,
		string json,
		CancellationToken cancellationToken = default,
		TimeSpan? timeout = null)
	{
		try
		{
			using var timeoutCts =
				new CancellationTokenSource(
					timeout ?? DefaultTimeout);

			using var linkedCts =
				CancellationTokenSource.CreateLinkedTokenSource(
					cancellationToken,
					timeoutCts.Token);

			CancellationToken requestToken =
				linkedCts.Token;

			using var content =
				new StringContent(
					json,
					System.Text.Encoding.UTF8,
					"application/json");

			using HttpResponseMessage response =
				await _httpClient
					.PostAsync(
						requestUri,
						content,
						requestToken)
					.ConfigureAwait(false);

			string responseContent =
				await response.Content
					.ReadAsStringAsync(requestToken)
					.ConfigureAwait(false);

			EnsureSuccess(
				response,
				responseContent);

			return responseContent;
		}
		catch (OperationCanceledException ex)
			when (!cancellationToken.IsCancellationRequested)
		{
			throw new ApiException(
				string.Empty,
				null,
				true,
				null,
				ex);
		}
		catch (HttpRequestException ex)
		{
			throw new ApiException(
				string.Empty,
				null,
				true,
				null,
				ex);
		}
	}

	private static async Task DelayRetryAsync(
		TimeSpan? retryAfter,
		int attempt,
		CancellationToken cancellationToken)
	{
		TimeSpan delay =
			retryAfter ?? attempt switch
			{
				1 => TimeSpan.FromMilliseconds(500),
				2 => TimeSpan.FromSeconds(2),
				_ => TimeSpan.FromSeconds(4)
			};

		// A broken server can return an arbitrarily long Retry-After value.
		if (delay < TimeSpan.Zero)
		{
			delay = TimeSpan.Zero;
		}

		if (delay > TimeSpan.FromSeconds(30))
		{
			delay = TimeSpan.FromSeconds(30);
		}

		await Task.Delay(
			delay,
			cancellationToken)
			.ConfigureAwait(false);
	}

	private static void EnsureSuccess(
		HttpResponseMessage response,
		string responseBody)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		int statusCode =
			(int)response.StatusCode;

		bool transient =
			statusCode is
				408 or
				425 or
				429 or
				500 or
				502 or
				503 or
				504;

		throw ApiException.FromResponse(
			statusCode,
			response.ReasonPhrase,
			responseBody,
			transient,
			ReadRetryAfter(response));
	}

	/// <summary>Retry-After comes either as seconds or as an absolute date.</summary>
	private static TimeSpan? ReadRetryAfter(
		HttpResponseMessage response)
	{
		if (response.Headers.RetryAfter is not { } header)
		{
			return null;
		}

		if (header.Delta is { } delta)
		{
			return delta;
		}

		return header.Date is { } date
			? date - DateTimeOffset.UtcNow
			: null;
	}

	private static HttpClient CreateHttpClient()
	{
		var client =
			new HttpClient
			{
				Timeout =
					Timeout.InfiniteTimeSpan
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