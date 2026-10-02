using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>A parsed response together with the status code the protocol keys on.</summary>
internal sealed class SchutzengelResponse : IDisposable
{
	public SchutzengelResponse(HttpStatusCode statusCode, JsonDocument json)
	{
		StatusCode = statusCode;
		Json = json;
	}

	public HttpStatusCode StatusCode { get; }

	public JsonDocument Json { get; }

	public JsonElement Root => Json.RootElement;

	public void Dispose() => Json.Dispose();
}

internal sealed class SchutzengelApi
{
	private const string BaseUrl =
		"https://m.dvb.de/schutzengel/";

	private const string BrowserUserAgent =
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
		"(KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 Edg/156.0.0.0";

	/// <summary>One request must never hang the polling loop.</summary>
	internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

	private static readonly SemaphoreSlim AuthenticationGate = new(1, 1);

	private readonly HttpClient _http;
	private readonly Func<CancellationToken, Task<string?>> _loadToken;
	private readonly Func<string, CancellationToken, Task> _saveToken;
	private readonly Action _removeToken;

	private volatile string? _token;

	internal SchutzengelApi(HttpClient http)
		: this(
			http,
			static _ => Task.FromResult<string?>(null),
			static (_, _) => Task.CompletedTask,
			static () => { })
	{
	}

	internal SchutzengelApi(
		HttpClient http,
		Func<CancellationToken, Task<string?>> loadToken,
		Func<string, CancellationToken, Task> saveToken,
		Action removeToken)
	{
		_http = http ?? throw new ArgumentNullException(nameof(http));
		_loadToken = loadToken ?? throw new ArgumentNullException(nameof(loadToken));
		_saveToken = saveToken ?? throw new ArgumentNullException(nameof(saveToken));
		_removeToken = removeToken ?? throw new ArgumentNullException(nameof(removeToken));
	}

	// ----- Authentication -----

	/// <summary>Uses a stored token if there is one; creates an anonymous account otherwise.</summary>
	public async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(_token))
		{
			return;
		}

		await AuthenticationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			if (!string.IsNullOrWhiteSpace(_token))
			{
				return;
			}

			string? stored = await _loadToken(cancellationToken).ConfigureAwait(false);

			if (!string.IsNullOrWhiteSpace(stored))
			{
				_token = stored.Trim();
				return;
			}

			string created = await CreateAccountAsync(cancellationToken).ConfigureAwait(false);

			await _saveToken(created, cancellationToken).ConfigureAwait(false);

			_token = created;

			Log("Created and persisted a new authentication token.");
		}
		finally
		{
			AuthenticationGate.Release();
		}
	}

	/// <summary>True if a token is already known locally. Never creates an account.</summary>
	public async Task<bool> HasAccountAsync(CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(_token))
		{
			return true;
		}

		string? stored = await _loadToken(cancellationToken).ConfigureAwait(false);

		if (string.IsNullOrWhiteSpace(stored))
		{
			return false;
		}

		_token = stored.Trim();
		return true;
	}

	public void Authenticate(string token)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(token);
		_token = token.Trim();
	}

	public Task<string> CreateAccountAsync(CancellationToken cancellationToken) =>
		CreateAccountCoreAsync(cancellationToken);

	// ----- Plans -----

	public Task<JsonDocument> CreatePlanAsync(string serializedPlan, CancellationToken cancellationToken) =>
		SendJsonAsync(HttpMethod.Post, "plans", serializedPlan, cancellationToken);

	public Task<JsonDocument> GetAllPlansAsync(CancellationToken cancellationToken) =>
		SendJsonAsync(HttpMethod.Get, "plansMinimal", null, cancellationToken);

	/// <summary>The stored raw connection and polyline of one plan.</summary>
	public Task<JsonDocument> GetPlanAsync(string planId, CancellationToken cancellationToken) =>
		SendJsonAsync(
			HttpMethod.Get,
			$"planRawData?plan_id={Uri.EscapeDataString(planId)}",
			null,
			cancellationToken,
			planId: planId,
			dataVersion: "1");

	public Task<JsonDocument> ActivateAsync(string planId, CancellationToken cancellationToken) =>
		SendJsonAsync(
			HttpMethod.Post,
			"activatePlan",
			JsonSerializer.Serialize(new { plan_id = planId }),
			cancellationToken);

	public Task<JsonDocument> DeactivateAsync(string planId, CancellationToken cancellationToken) =>
		SendJsonAsync(
			HttpMethod.Post,
			"deactivatePlan",
			JsonSerializer.Serialize(new { plan_id = planId }),
			cancellationToken);

	/// <summary>
	/// The reference client posts the complete options object as <c>newOptions</c>
	/// next to the plan id; the id alone changes nothing.
	/// </summary>
	public Task<JsonDocument> SetOptionsAsync(
		string planId,
		SchutzengelOptions options,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(options);

		return SendJsonAsync(
			HttpMethod.Post,
			"planSetOptions",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId,
					newOptions = options.ToPayload()
				}),
			cancellationToken);
	}

	public Task<JsonDocument> DeletePlanAsync(string planId, CancellationToken cancellationToken) =>
		SendJsonAsync(
			HttpMethod.Delete,
			"plan",
			JsonSerializer.Serialize(new { plan_id = planId }),
			cancellationToken);

	public Task<JsonDocument> DeleteAllPlansAsync(CancellationToken cancellationToken) =>
		SendJsonAsync(HttpMethod.Delete, "allPlans", null, cancellationToken);

	// ----- Trips -----

	/// <summary>Status 201 means "unchanged since <paramref name="dataVersion"/>"; the body is then empty.</summary>
	public Task<SchutzengelResponse> FetchRealtimeAsync(
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
			notificationCount: notificationCount?.ToString(CultureInfo.InvariantCulture));

	/// <summary>Status 204 means "no new notifications".</summary>
	public Task<SchutzengelResponse> FetchNotificationsAsync(
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
			notificationCount: notificationCount?.ToString(CultureInfo.InvariantCulture));

	public Task<JsonDocument> GetRealtimeAsync(string tripId, CancellationToken cancellationToken) =>
		GetRealtimeAsync(tripId, null, null, cancellationToken);

	public async Task<JsonDocument> GetRealtimeAsync(
		string tripId,
		string? dataVersion,
		int? notificationCount,
		CancellationToken cancellationToken) =>
		(await FetchRealtimeAsync(tripId, dataVersion, notificationCount, cancellationToken)
			.ConfigureAwait(false)).Json;

	public Task<JsonDocument> GetNotificationsAsync(string tripId, CancellationToken cancellationToken) =>
		GetNotificationsAsync(tripId, null, null, cancellationToken);

	public async Task<JsonDocument> GetNotificationsAsync(
		string tripId,
		string? dataVersion,
		int? notificationCount,
		CancellationToken cancellationToken) =>
		(await FetchNotificationsAsync(tripId, dataVersion, notificationCount, cancellationToken)
			.ConfigureAwait(false)).Json;

	/// <summary>The service answers with the bare epoch milliseconds, which is valid JSON.</summary>
	public Task<JsonDocument> GetServerTimeAsync(CancellationToken cancellationToken) =>
		SendJsonAsync(HttpMethod.Get, "serverTime", null, cancellationToken);

	// ----- Push registration (kept for parity with the reference client) -----

	public Task<JsonDocument> RegisterFirebaseAsync(string token, CancellationToken cancellationToken) =>
		SendJsonAsync(
			HttpMethod.Post,
			"register-firebase",
			JsonSerializer.Serialize(new { token }),
			cancellationToken);

	public Task<JsonDocument> UnregisterFirebaseAsync(string token, CancellationToken cancellationToken) =>
		SendJsonAsync(
			HttpMethod.Post,
			"unregister-firebase",
			JsonSerializer.Serialize(new { token }),
			cancellationToken);

	// ----- Transport -----

	private async Task<string> CreateAccountCoreAsync(CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "api/create-account");

		request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
		request.Headers.Accept.ParseAdd("application/json, text/plain, */*");

		LogRequest(request.Method, "api/create-account", "{}");

		using CancellationTokenSource timeout = CreateTimeout(cancellationToken);

		using HttpResponseMessage response =
			await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

		string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"Schutzengel account creation HTTP {(int)response.StatusCode}: {body}",
				null,
				response.StatusCode);
		}

		string token = body.Trim().Trim('"').Trim();

		if (string.IsNullOrWhiteSpace(token))
		{
			throw new InvalidOperationException("Schutzengel returned an empty account token.");
		}

		return token;
	}

	private async Task<JsonDocument> SendJsonAsync(
		HttpMethod method,
		string path,
		string? body,
		CancellationToken cancellationToken,
		string? planId = null,
		string? tripId = null,
		string? dataVersion = null,
		string? notificationCount = null)
	{
		SchutzengelResponse response =
			await SendAsync(
				method, path, body, cancellationToken,
				planId, tripId, dataVersion, notificationCount).ConfigureAwait(false);

		return response.Json;
	}

	private async Task<SchutzengelResponse> SendAsync(
		HttpMethod method,
		string path,
		string? body,
		CancellationToken cancellationToken,
		string? planId = null,
		string? tripId = null,
		string? dataVersion = null,
		string? notificationCount = null)
	{
		await EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);

		string token = _token
			?? throw new InvalidOperationException("Schutzengel account has not been initialized.");

		(HttpStatusCode Status, string Text) result =
			await SendAuthenticatedAsync(
				method, path, body, token, cancellationToken,
				planId, tripId, dataVersion, notificationCount).ConfigureAwait(false);

		if (result.Status == HttpStatusCode.Unauthorized)
		{
			Log("Authentication rejected (401); replacing the token once.");

			await RefreshAfterAuthenticationFailureAsync(token, cancellationToken).ConfigureAwait(false);

			string retryToken = _token
				?? throw new InvalidOperationException(
					"Schutzengel authentication recovery did not produce a token.");

			result =
				await SendAuthenticatedAsync(
					method, path, body, retryToken, cancellationToken,
					planId, tripId, dataVersion, notificationCount).ConfigureAwait(false);
		}

		if ((int)result.Status is < 200 or >= 300)
		{
			throw new HttpRequestException(
				$"Schutzengel HTTP {(int)result.Status} on {method.Method} {path}: {result.Text}",
				null,
				result.Status);
		}

		return new SchutzengelResponse(result.Status, ParseBody(result.Text));
	}

	/// <summary>
	/// Any valid JSON is accepted (objects, arrays, bare numbers such as the server time).
	/// Anything else becomes a JSON string, an empty body an empty object.
	/// </summary>
	internal static JsonDocument ParseBody(string body)
	{
		string trimmed = body.Trim();

		if (trimmed.Length == 0)
		{
			return JsonDocument.Parse("{}");
		}

		try
		{
			return JsonDocument.Parse(trimmed);
		}
		catch (JsonException)
		{
			return JsonDocument.Parse(JsonSerializer.Serialize(trimmed));
		}
	}

	private async Task RefreshAfterAuthenticationFailureAsync(
		string failedToken,
		CancellationToken cancellationToken)
	{
		await AuthenticationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			// Another request may have replaced the token while this one waited.
			if (!string.IsNullOrWhiteSpace(_token)
				&& !string.Equals(_token, failedToken, StringComparison.Ordinal))
			{
				return;
			}

			string? stored = await _loadToken(cancellationToken).ConfigureAwait(false);

			if (!string.IsNullOrWhiteSpace(stored)
				&& !string.Equals(stored.Trim(), failedToken, StringComparison.Ordinal))
			{
				_token = stored.Trim();
				return;
			}

			_removeToken();
			_token = null;

			string created = await CreateAccountAsync(cancellationToken).ConfigureAwait(false);

			await _saveToken(created, cancellationToken).ConfigureAwait(false);

			_token = created;

			Log("Replacement authentication token created and persisted.");
		}
		finally
		{
			AuthenticationGate.Release();
		}
	}

	private async Task<(HttpStatusCode Status, string Text)> SendAuthenticatedAsync(
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
		using HttpRequestMessage request = CreateRequest(method, path);

		request.Headers.TryAddWithoutValidation("Authentication", $"Bearer {token}");

		if (planId is not null)
		{
			request.Headers.TryAddWithoutValidation("plan_id", planId);
		}

		if (tripId is not null)
		{
			request.Headers.TryAddWithoutValidation("trip_id", tripId);
		}

		if (dataVersion is not null)
		{
			request.Headers.TryAddWithoutValidation("data_version", dataVersion);
		}

		if (notificationCount is not null)
		{
			request.Headers.TryAddWithoutValidation("notification_count", notificationCount);
		}

		if (body is not null)
		{
			request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		}

		LogRequest(method, path, body);

		// One deadline covers sending and reading the body.
		using CancellationTokenSource timeout = CreateTimeout(cancellationToken);

		using HttpResponseMessage response =
			await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);

		string text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

		return (response.StatusCode, text);
	}

	private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
	{
		CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		source.CancelAfter(RequestTimeout);
		return source;
	}

	[Conditional("DEBUG")]
	private static void LogRequest(HttpMethod method, string path, string? body)
	{
		const int Limit = 1500;

		string preview =
			body is null
				? "-"
				: body.Length <= Limit
					? body
					: body[..Limit] + $"... (+{body.Length - Limit} chars)";

		Debug.WriteLine($"[SCHUTZENGEL] {method.Method} {path} {preview}");
	}

	[Conditional("DEBUG")]
	private static void Log(string message) =>
		Debug.WriteLine($"[SCHUTZENGEL] {message}");

	private static HttpRequestMessage CreateRequest(HttpMethod method, string path)
	{
		var request = new HttpRequestMessage(method, new Uri(new Uri(BaseUrl), path));

		request.Headers.TryAddWithoutValidation("Accept", "*/*");
		request.Headers.TryAddWithoutValidation("Accept-Language", "en,en-US;q=0.9,de;q=0.8,de-DE;q=0.7,sq;q=0.6");
		request.Headers.TryAddWithoutValidation("DNT", "1");
		request.Headers.TryAddWithoutValidation("Origin", "https://m.dvb.de");
		request.Headers.TryAddWithoutValidation("Referer", "https://m.dvb.de/");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
		request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
		request.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
		request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"156\", \"Microsoft Edge\";v=\"156\"");
		request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
		request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");

		return request;
	}
}
