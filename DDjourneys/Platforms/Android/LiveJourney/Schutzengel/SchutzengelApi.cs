using System.Net;
using System.Text;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelApi
{
	private const string BaseUrl =
		"https://m.dvb.de/schutzengel/";

	private const string BrowserUserAgent =
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
		"(KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 Edg/156.0.0.0";

	private static readonly SemaphoreSlim AuthenticationGate =
		new(1, 1);

	private readonly HttpClient _http;

	private readonly Func<
		CancellationToken,
		Task<string?>> _loadToken;

	private readonly Func<
		string,
		CancellationToken,
		Task> _saveToken;

	private readonly Action _removeToken;

	private string? _token;


	internal SchutzengelApi(
		HttpClient http)
		: this(
			http,
			static _ =>
				Task.FromResult<string?>(null),
			static (_, _) =>
				Task.CompletedTask,
			static () =>
			{
			})
	{
	}


	internal SchutzengelApi(
		HttpClient http,
		Func<
			CancellationToken,
			Task<string?>> loadToken,
		Func<
			string,
			CancellationToken,
			Task> saveToken,
		Action removeToken)
	{
		_http =
			http
			?? throw new ArgumentNullException(
				nameof(http));

		_loadToken =
			loadToken
			?? throw new ArgumentNullException(
				nameof(loadToken));

		_saveToken =
			saveToken
			?? throw new ArgumentNullException(
				nameof(saveToken));

		_removeToken =
			removeToken
			?? throw new ArgumentNullException(
				nameof(removeToken));
	}


	public async Task EnsureAuthenticatedAsync(
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(_token))
		{
			return;
		}


		await AuthenticationGate.WaitAsync(
			cancellationToken);

		try
		{
			if (!string.IsNullOrWhiteSpace(_token))
			{
				return;
			}


			string? storedToken =
				await _loadToken(
					cancellationToken);

			if (!string.IsNullOrWhiteSpace(
				storedToken))
			{
				_token =
					storedToken.Trim();

				System.Diagnostics.Debug.WriteLine(
					"[SCHUTZENGEL] Reusing persisted authentication token.");

				return;
			}


			string newToken =
				await CreateAccountAsync(
					cancellationToken);


			await _saveToken(
				newToken,
				cancellationToken);


			_token =
				newToken;

			System.Diagnostics.Debug.WriteLine(
				"[SCHUTZENGEL] Created and persisted new authentication token.");
		}
		finally
		{
			AuthenticationGate.Release();
		}
	}


	public void Authenticate(
		string token)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(
			token);

		_token =
			token.Trim();
	}


	public Task<string> CreateAccountAsync(
		CancellationToken cancellationToken) =>
		CreateAccountCoreAsync(
			cancellationToken);


	public Task<JsonDocument> CreatePlanAsync(
		string serializedPlan,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Post,
			"plans",
			serializedPlan,
			cancellationToken);


	public Task<JsonDocument> GetRealtimeAsync(
		string tripId,
		CancellationToken cancellationToken) =>
		GetRealtimeAsync(
			tripId,
			null,
			null,
			cancellationToken);


	public Task<JsonDocument> GetRealtimeAsync(
		string tripId,
		string? dataVersion,
		int? notificationCount,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Get,
			$"planRealtime?trip_id={Uri.EscapeDataString(tripId)}",
			null,
			cancellationToken,
			tripId: tripId,
			dataVersion: dataVersion,
			notificationCount:
				notificationCount?.ToString(
					System.Globalization.CultureInfo.InvariantCulture));


	public Task<JsonDocument> GetNotificationsAsync(
		string tripId,
		CancellationToken cancellationToken) =>
		GetNotificationsAsync(
			tripId,
			null,
			null,
			cancellationToken);


	public Task<JsonDocument> GetNotificationsAsync(
		string tripId,
		string? dataVersion,
		int? notificationCount,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Get,
			"notifications",
			null,
			cancellationToken,
			tripId: tripId,
			dataVersion: dataVersion,
			notificationCount:
				notificationCount?.ToString(
					System.Globalization.CultureInfo.InvariantCulture));


	public Task<JsonDocument> GetServerTimeAsync(
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Get,
			"serverTime",
			null,
			cancellationToken);


	public Task<JsonDocument> GetAllPlansAsync(
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Get,
			"plansMinimal",
			null,
			cancellationToken);


	public Task<JsonDocument> DeleteAllPlansAsync(
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Delete,
			"allPlans",
			null,
			cancellationToken);


	public Task<JsonDocument> GetPlanAsync(
		string planId,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Get,
			$"planRawData?plan_id={Uri.EscapeDataString(planId)}",
			null,
			cancellationToken,
			planId: planId,
			dataVersion: "1");


	public Task<JsonDocument> ActivateAsync(
		string planId,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Post,
			"activatePlan",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId
				}),
			cancellationToken);


	public Task<JsonDocument> DeactivateAsync(
		string planId,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Post,
			"deactivatePlan",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId
				}),
			cancellationToken);


	public Task<JsonDocument> SetOptionsAsync(
		string planId,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Post,
			"planSetOptions",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId
				}),
			cancellationToken);


	public Task<JsonDocument> DeletePlanAsync(
		string planId,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Delete,
			"plan",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId
				}),
			cancellationToken);


	public Task<JsonDocument> RegisterFirebaseAsync(
		string token,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Post,
			"register-firebase",
			JsonSerializer.Serialize(
				new
				{
					token
				}),
			cancellationToken);


	public Task<JsonDocument> UnregisterFirebaseAsync(
		string token,
		CancellationToken cancellationToken) =>
		SendAsync(
			HttpMethod.Post,
			"unregister-firebase",
			JsonSerializer.Serialize(
				new
				{
					token
				}),
			cancellationToken);


	private async Task<string> CreateAccountCoreAsync(
		CancellationToken cancellationToken)
	{
		using HttpRequestMessage request =
			CreateRequest(
				HttpMethod.Post,
				"api/create-account");


		request.Content =
			new StringContent(
				"{}",
				Encoding.UTF8,
				"application/json");


		request.Headers.Accept.ParseAdd(
			"application/json, text/plain, */*");


		LogRequest(
			request.Method,
			"api/create-account",
			"{}");


		using HttpResponseMessage response =
			await _http.SendAsync(
				request,
				cancellationToken);


		string responseBody =
			await response.Content.ReadAsStringAsync(
				cancellationToken);


		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"Schutzengel account creation HTTP " +
				$"{(int)response.StatusCode}: {responseBody}");
		}


		string token =
			responseBody
				.Trim()
				.Trim('"')
				.Trim();


		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException(
				"Schutzengel returned an empty account token.");
		}


		return token;
	}


	private async Task<JsonDocument> SendAsync(
		HttpMethod method,
		string path,
		string? body,
		CancellationToken cancellationToken,
		string? planId = null,
		string? tripId = null,
		string? dataVersion = null,
		string? notificationCount = null)
	{
		await EnsureAuthenticatedAsync(
			cancellationToken);


		string token =
			_token
			?? throw new InvalidOperationException(
				"Schutzengel account has not been initialized.");


		HttpResponseMessage response =
			await SendAuthenticatedRequestAsync(
				method,
				path,
				body,
				token,
				cancellationToken,
				planId,
				tripId,
				dataVersion,
				notificationCount);


		if (response.StatusCode ==
			HttpStatusCode.Unauthorized)
		{
			response.Dispose();


			System.Diagnostics.Debug.WriteLine(
				"[SCHUTZENGEL] Authentication rejected with HTTP 401; " +
				"discarding token and retrying once with a new token.");


			await RefreshAfterAuthenticationFailureAsync(
				token,
				cancellationToken);


			string retryToken =
				_token
				?? throw new InvalidOperationException(
					"Schutzengel authentication recovery did not produce a token.");


			response =
				await SendAuthenticatedRequestAsync(
					method,
					path,
					body,
					retryToken,
					cancellationToken,
					planId,
					tripId,
					dataVersion,
					notificationCount);
		}


		using (response)
		{
			string responseBody =
				await response.Content.ReadAsStringAsync(
					cancellationToken);


			if (!response.IsSuccessStatusCode)
			{
				throw new HttpRequestException(
					$"Schutzengel HTTP " +
					$"{(int)response.StatusCode} " +
					$"on {method.Method} {path}: " +
					$"{responseBody}");
			}


			responseBody =
				responseBody.Trim();


			if (responseBody.Length == 0)
			{
				return JsonDocument.Parse(
					"{}");
			}


			if (responseBody[0] is '{' or '[')
			{
				return JsonDocument.Parse(
					responseBody);
			}


			return JsonDocument.Parse(
				"{}");
		}
	}


	private async Task RefreshAfterAuthenticationFailureAsync(
		string failedToken,
		CancellationToken cancellationToken)
	{
		await AuthenticationGate.WaitAsync(
			cancellationToken);

		try
		{
			if (!string.IsNullOrWhiteSpace(_token)
				&& !string.Equals(
					_token,
					failedToken,
					StringComparison.Ordinal))
			{
				return;
			}


			string? storedToken =
				await _loadToken(
					cancellationToken);


			if (!string.IsNullOrWhiteSpace(
				storedToken)
				&& !string.Equals(
					storedToken.Trim(),
					failedToken,
					StringComparison.Ordinal))
			{
				_token =
					storedToken.Trim();


				System.Diagnostics.Debug.WriteLine(
					"[SCHUTZENGEL] Adopted authentication token refreshed by another request.");

				return;
			}


			_removeToken();

			_token = null;


			string newToken =
				await CreateAccountAsync(
					cancellationToken);


			await _saveToken(
				newToken,
				cancellationToken);


			_token =
				newToken;


			System.Diagnostics.Debug.WriteLine(
				"[SCHUTZENGEL] Replacement authentication token created and persisted.");
		}
		finally
		{
			AuthenticationGate.Release();
		}
	}


	private async Task<HttpResponseMessage> SendAuthenticatedRequestAsync(
		HttpMethod method,
		string path,
		string? body,
		string token,
		CancellationToken cancellationToken,
		string? planId,
		string? tripId,
		string? dataVersion,
		string? notificationCount)
	{
		HttpRequestMessage request =
			CreateRequest(
				method,
				path);


		request.Headers.TryAddWithoutValidation(
			"Authentication",
			$"Bearer {token}");


		if (planId is not null)
		{
			request.Headers.TryAddWithoutValidation(
				"plan_id",
				planId);
		}


		if (tripId is not null)
		{
			request.Headers.TryAddWithoutValidation(
				"trip_id",
				tripId);
		}


		if (dataVersion is not null)
		{
			request.Headers.TryAddWithoutValidation(
				"data_version",
				dataVersion);
		}


		if (notificationCount is not null)
		{
			request.Headers.TryAddWithoutValidation(
				"notification_count",
				notificationCount);
		}


		if (body is not null)
		{
			request.Content =
				new StringContent(
					body,
					Encoding.UTF8,
					"application/json");
		}


		LogRequest(
			method,
			path,
			body);


		try
		{
			HttpResponseMessage response =
				await _http.SendAsync(
					request,
					cancellationToken);

			request.Dispose();

			return response;
		}
		catch
		{
			request.Dispose();

			throw;
		}
	}


	private static void LogRequest(
		HttpMethod method,
		string path,
		string? body)
	{
		System.Diagnostics.Debug.WriteLine(
			$"[SCHUTZENGEL REQUEST BEGIN] {method.Method} {path}");

		LogLong(
			body);

		System.Diagnostics.Debug.WriteLine(
			"[SCHUTZENGEL REQUEST END]\n");
	}


	private static void LogLong(
		string? text)
	{
		const int ChunkSize =
			2048;


		if (text is null)
		{
			System.Diagnostics.Debug.WriteLine(
				"null");

			return;
		}


		for (
			int i = 0;
			i < text.Length;
			i += ChunkSize)
		{
			System.Diagnostics.Debug.WriteLine(
				text.Substring(
					i,
					Math.Min(
						ChunkSize,
						text.Length - i)));
		}
	}


	private static HttpRequestMessage CreateRequest(
		HttpMethod method,
		string path)
	{
		var request =
			new HttpRequestMessage(
				method,
				new Uri(
					new Uri(BaseUrl),
					path));


		request.Headers.TryAddWithoutValidation(
			"Accept",
			"*/*");


		request.Headers.TryAddWithoutValidation(
			"Accept-Language",
			"en,en-US;q=0.9,de;q=0.8,de-DE;q=0.7,sq;q=0.6");


		request.Headers.TryAddWithoutValidation(
			"DNT",
			"1");


		request.Headers.TryAddWithoutValidation(
			"Origin",
			"https://m.dvb.de");


		request.Headers.TryAddWithoutValidation(
			"Referer",
			"https://m.dvb.de/");


		request.Headers.TryAddWithoutValidation(
			"Sec-Fetch-Dest",
			"empty");


		request.Headers.TryAddWithoutValidation(
			"Sec-Fetch-Mode",
			"cors");


		request.Headers.TryAddWithoutValidation(
			"Sec-Fetch-Site",
			"same-origin");


		request.Headers.TryAddWithoutValidation(
			"User-Agent",
			BrowserUserAgent);


		request.Headers.TryAddWithoutValidation(
			"sec-ch-ua",
			"\"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"156\", \"Microsoft Edge\";v=\"156\"");


		request.Headers.TryAddWithoutValidation(
			"sec-ch-ua-mobile",
			"?0");


		request.Headers.TryAddWithoutValidation(
			"sec-ch-ua-platform",
			"\"Windows\"");


		return request;
	}
}