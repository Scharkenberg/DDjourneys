using DDjourneys.Core.Diagnostics;
using System.Diagnostics;
using Microsoft.Maui.Storage;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Persists the anonymous account token. Only the token lives here; everything else about a
/// followed journey is stored by the service itself and reloaded from there, so the token IS the user's
/// followed journeys: losing it (a keystore that cannot be read after a restore, an OS update, a new
/// lock screen) would lose all of them. The token therefore lives in the secure store AND, as a fallback,
/// in the app's private preferences (it is an anonymous identifier, not a credential of the person); it is
/// only discarded when the service itself rejects it (<see cref="RemoveToken"/>).
/// </summary>
internal static class SchutzengelTokenStore
{
	private const string TokenKey = "schutzengel_auth_token";
	private const string FallbackKey = "schutzengel_auth_token.fallback";

	public static async Task<string?> GetAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		string? secure = null;

		try
		{
			secure = await SecureStorage.Default.GetAsync(TokenKey).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// A keystore that cannot decrypt (restored backup, reset lock screen) throws. Drop the unreadable
			// entry; the fallback copy below still has the token.
			DiagnosticLog.Write($"[SCHUTZENGEL] Token storage unreadable: {ex.Message}");

			RemoveSecure();
		}

		if (secure is { Length: > 0 })
		{
			Remember(secure);

			return secure;
		}

		string? fallback = ReadFallback();

		if (fallback is null)
		{
			return null;
		}

		// Heal the secure copy so the next start reads it normally.
		try
		{
			await SecureStorage.Default.SetAsync(TokenKey, fallback).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[SCHUTZENGEL] Token not restored to secure storage: {ex.Message}");
		}

		return fallback;
	}

	public static async Task SetAsync(string value, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		ArgumentException.ThrowIfNullOrWhiteSpace(value);

		Remember(value);

		try
		{
			await SecureStorage.Default.SetAsync(TokenKey, value).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			// The fallback copy carries the token until the secure store works again.
			DiagnosticLog.Write($"[SCHUTZENGEL] Token not saved to secure storage: {ex.Message}");
		}
	}

	/// <summary>The service rejected the token: both copies go.</summary>
	public static void RemoveToken()
	{
		RemoveSecure();

		try
		{
			Preferences.Default.Remove(FallbackKey);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[SCHUTZENGEL] Token fallback removal failed: {ex.Message}");
		}
	}

	private static void RemoveSecure()
	{
		try
		{
			SecureStorage.Default.Remove(TokenKey);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[SCHUTZENGEL] Token removal failed: {ex.Message}");
		}
	}

	private static void Remember(string value)
	{
		try
		{
			if (Preferences.Default.Get(FallbackKey, string.Empty) != value)
			{
				Preferences.Default.Set(FallbackKey, value);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[SCHUTZENGEL] Token fallback not saved: {ex.Message}");
		}
	}

	private static string? ReadFallback()
	{
		try
		{
			string value = Preferences.Default.Get(FallbackKey, string.Empty);

			return value.Length == 0 ? null : value;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[SCHUTZENGEL] Token fallback unreadable: {ex.Message}");

			return null;
		}
	}
}
