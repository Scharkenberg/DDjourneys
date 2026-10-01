using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelApi(HttpClient http)
{
	private const string BaseUrl = "https://schutzengel.ivi.fraunhofer.de/api/";
	private string? _token;

	public async Task<string> CreateAccountAsync(CancellationToken ct)
	{
		using var response = await http.PostAsync(new Uri(new Uri(BaseUrl), "create-account"), new StringContent("", Encoding.UTF8, "application/json"), ct);
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
	}

	public void Authenticate(string token) => _token = token;
	public Task<JsonDocument> CreatePlanAsync(string serializedPlan, CancellationToken ct) => SendAsync(HttpMethod.Post, "plans", serializedPlan, ct);
	public Task<JsonDocument> GetRealtimeAsync(string tripId, CancellationToken ct) => SendAsync(HttpMethod.Get, $"planRealtime?trip_id={Uri.EscapeDataString(tripId)}", null, ct);
	public Task<JsonDocument> GetNotificationsAsync(string tripId, CancellationToken ct) => SendAsync(HttpMethod.Get, $"notifications?trip_id={Uri.EscapeDataString(tripId)}", null, ct);
	public Task<JsonDocument> GetServerTimeAsync(CancellationToken ct) => SendAsync(HttpMethod.Get, "serverTime", null, ct);
	public Task<JsonDocument> GetAllPlansAsync(CancellationToken ct) => SendAsync(HttpMethod.Get, "plansMinimal", null, ct);
	public Task<JsonDocument> DeleteAllPlansAsync(CancellationToken ct) => SendAsync(HttpMethod.Delete, "allPlans", null, ct);
	public Task<JsonDocument> GetPlanAsync(string planId, CancellationToken ct) => SendAsync(HttpMethod.Get, $"planRawData?plan_id={Uri.EscapeDataString(planId)}", null, ct);
	public Task<JsonDocument> ActivateAsync(string planId, CancellationToken ct) => SendAsync(HttpMethod.Post, "activatePlan", JsonSerializer.Serialize(new { plan_id = planId }), ct);
	public Task<JsonDocument> DeactivateAsync(string planId, CancellationToken ct) => SendAsync(HttpMethod.Post, "deactivatePlan", JsonSerializer.Serialize(new { plan_id = planId }), ct);
	public Task<JsonDocument> SetOptionsAsync(string planId, CancellationToken ct) => SendAsync(HttpMethod.Post, "planSetOptions", JsonSerializer.Serialize(new { plan_id = planId }), ct);
	public Task<JsonDocument> DeletePlanAsync(string planId, CancellationToken ct) => SendAsync(HttpMethod.Delete, $"plan?plan_id={Uri.EscapeDataString(planId)}", null, ct);
	public Task<JsonDocument> RegisterFirebaseAsync(string token, CancellationToken ct) => SendAsync(HttpMethod.Post, "register-firebase", JsonSerializer.Serialize(new { token }), ct);
	public Task<JsonDocument> UnregisterFirebaseAsync(string token, CancellationToken ct) => SendAsync(HttpMethod.Post, "unregister-firebase", JsonSerializer.Serialize(new { token }), ct);

	private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string? body, CancellationToken ct)
	{
		using var request = new HttpRequestMessage(method, new Uri(new Uri(BaseUrl), path));
		if (_token is null) throw new InvalidOperationException("Schutzengel account has not been initialized.");
		request.Headers.TryAddWithoutValidation("Authentication", $"Bearer {_token}");
		if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		using var response = await http.SendAsync(request, ct);
		response.EnsureSuccessStatusCode();
		return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
	}
}
