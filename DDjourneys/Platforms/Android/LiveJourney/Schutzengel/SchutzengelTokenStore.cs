using System.Diagnostics;
using Microsoft.Maui.Storage;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>
/// Persists the anonymous account token. Only the token lives here; everything else about a
/// followed journey is stored by the service itself and reloaded from there.
/// </summary>
internal sealed class SchutzengelTokenStore
{
	private const string TokenKey = "schutzengel_auth_token";

	public async Task<string?> GetAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			return await SecureStorage.Default.GetAsync(TokenKey).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// A keystore that cannot decrypt (restored backup, reset lock screen) throws.
			// The token is only an anonymous account, so starting over is the right recovery.
			Debug.WriteLine($"[SCHUTZENGEL] Token storage unreadable, discarding it: {ex.Message}");

			RemoveToken();

			return null;
		}
	}

	public Task SetAsync(string value, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		return SecureStorage.Default.SetAsync(TokenKey, value);
	}

	public void RemoveToken()
	{
		try
		{
			SecureStorage.Default.Remove(TokenKey);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[SCHUTZENGEL] Token removal failed: {ex.Message}");
		}
	}
}
