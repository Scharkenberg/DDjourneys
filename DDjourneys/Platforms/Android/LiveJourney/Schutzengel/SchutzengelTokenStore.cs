using Microsoft.Maui.Storage;
using DDjourneys.Core.Models;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelTokenStore
{
	private const string Key = "schutzengel_auth_token";
	public Task<string?> GetAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return SecureStorage.Default.GetAsync(Key); }
	public Task SetAsync(string value, CancellationToken token) { token.ThrowIfCancellationRequested(); return SecureStorage.Default.SetAsync(Key, value); }
	public Task SaveJourneyAsync(Journey journey, CancellationToken token) { token.ThrowIfCancellationRequested(); return SecureStorage.Default.SetAsync("schutzengel_active_journey_snapshot", JsonSerializer.Serialize(journey)); }
	public async Task<Journey?> GetJourneyAsync(CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		var json = await SecureStorage.Default.GetAsync("schutzengel_active_journey_snapshot");
		return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Journey>(json);
	}
	public void RemoveJourney() => SecureStorage.Default.Remove("schutzengel_active_journey_snapshot");
}
