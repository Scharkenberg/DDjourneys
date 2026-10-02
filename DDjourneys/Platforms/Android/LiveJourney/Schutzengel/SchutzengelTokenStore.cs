using Microsoft.Maui.Storage;
using DDjourneys.Core.Models;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal sealed class SchutzengelTokenStore
{
	private const string TokenKey =
		"schutzengel_auth_token";

	private const string JourneyKey =
		"schutzengel_active_journey_snapshot";


	public Task<string?> GetAsync(
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		return SecureStorage.Default.GetAsync(
			TokenKey);
	}


	public Task SetAsync(
		string value,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		ArgumentException.ThrowIfNullOrWhiteSpace(
			value);

		return SecureStorage.Default.SetAsync(
			TokenKey,
			value);
	}


	public void RemoveToken()
	{
		SecureStorage.Default.Remove(
			TokenKey);
	}


	public Task SaveJourneyAsync(
		Journey journey,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		ArgumentNullException.ThrowIfNull(
			journey);

		return SecureStorage.Default.SetAsync(
			JourneyKey,
			JsonSerializer.Serialize(journey));
	}


	public async Task<Journey?> GetJourneyAsync(
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		string? json =
			await SecureStorage.Default.GetAsync(
				JourneyKey);

		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		return JsonSerializer.Deserialize<Journey>(
			json);
	}


	public void RemoveJourney()
	{
		SecureStorage.Default.Remove(
			JourneyKey);
	}
}