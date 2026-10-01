using System.Text;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelApi(HttpClient http)
{
	private const string BaseUrl =
		"https://m.dvb.de/schutzengel/";

	private const string BrowserUserAgent =
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
		"(KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 Edg/156.0.0.0";

	private string? _token;


	public async Task<string> CreateAccountAsync(
		CancellationToken ct)
	{
		using var request =
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

		using var response =
			await http.SendAsync(
				request,
				ct);

		response.EnsureSuccessStatusCode();

		return
			(await response.Content.ReadAsStringAsync(ct))
				.Trim()
				.Trim('"');
	}


	public void Authenticate(string token) =>
		_token = token;


	public Task<JsonDocument> CreatePlanAsync(
		string serializedPlan,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Post,
			"plans",
			serializedPlan,
			ct);


	public Task<JsonDocument> GetRealtimeAsync(
		string tripId,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Get,
			$"planRealtime?trip_id={Uri.EscapeDataString(tripId)}",
			null,
			ct,
			tripId: tripId,
			dataVersion: "2",
			notificationCount: "3");


	public Task<JsonDocument> GetNotificationsAsync(
		string tripId,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Get,
			"notifications",
			null,
			ct,
			tripId: tripId,
			dataVersion: "2",
			notificationCount: "3");


	public Task<JsonDocument> GetServerTimeAsync(
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Get,
			"serverTime",
			null,
			ct);


	public Task<JsonDocument> GetAllPlansAsync(
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Get,
			"plansMinimal",
			null,
			ct);


	public Task<JsonDocument> DeleteAllPlansAsync(
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Delete,
			"allPlans",
			null,
			ct);


	public Task<JsonDocument> GetPlanAsync(
		string planId,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Get,
			$"planRawData?plan_id={Uri.EscapeDataString(planId)}",
			null,
			ct,
			planId: planId,
			dataVersion: "1");


	public Task<JsonDocument> ActivateAsync(
		string planId,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Post,
			"activatePlan",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId
				}),
			ct);


	public Task<JsonDocument> DeactivateAsync(
		string planId,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Post,
			"deactivatePlan",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId
				}),
			ct);


	public Task<JsonDocument> SetOptionsAsync(
		string planId,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Post,
			"planSetOptions",
			JsonSerializer.Serialize(
				new
				{
					plan_id = planId
				}),
			ct);


	public Task<JsonDocument> DeletePlanAsync(
		string planId,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Delete,
			$"plan?plan_id={Uri.EscapeDataString(planId)}",
			null,
			ct);


	public Task<JsonDocument> RegisterFirebaseAsync(
		string token,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Post,
			"register-firebase",
			JsonSerializer.Serialize(
				new
				{
					token
				}),
			ct);


	public Task<JsonDocument> UnregisterFirebaseAsync(
		string token,
		CancellationToken ct) =>
		SendAsync(
			HttpMethod.Post,
			"unregister-firebase",
			JsonSerializer.Serialize(
				new
				{
					token
				}),
			ct);


	private async Task<JsonDocument> SendAsync(
		HttpMethod method,
		string path,
		string? body,
		CancellationToken ct,
		string? planId = null,
		string? tripId = null,
		string? dataVersion = null,
		string? notificationCount = null)
	{
		if (_token is null)
		{
			throw new InvalidOperationException(
				"Schutzengel account has not been initialized.");
		}


		using var request =
			CreateRequest(
				method,
				path);


		request.Headers.TryAddWithoutValidation(
			"Authentication",
			$"Bearer {_token}");


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

		System.Diagnostics.Debug.WriteLine("[SCHUTZENGEL REQUEST BEGIN]");
		LogLong(body);
		System.Diagnostics.Debug.WriteLine("[SCHUTZENGEL REQUEST END]\n");

		using var response =
	await http.SendAsync(
		request,
		ct);

		string responseBody =
			await response.Content.ReadAsStringAsync(
				ct);

		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException(
				$"Schutzengel HTTP {(int)response.StatusCode}: {responseBody}");
		}

		responseBody =
			responseBody.Trim();

		if (responseBody.Length == 0)
		{
			return JsonDocument.Parse("{}");
		}

		if (responseBody[0] is '{' or '[')
		{
			return JsonDocument.Parse(responseBody);
		}

		return JsonDocument.Parse("{}");
	}

	private static void LogLong(string? text)
	{
		const int ChunkSize = 2048;

		if (text is null)
		{
			System.Diagnostics.Debug.WriteLine("null");
			return;
		}
		for (int i = 0; i < text.Length; i += ChunkSize)
		{
			System.Diagnostics.Debug.WriteLine(text.Substring(i, Math.Min(ChunkSize, text.Length - i)));
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