using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// Notices a start that never finished. <see cref="Begin"/> runs first thing in <c>MauiProgram</c>, <see cref="Complete"/>
/// when the app has been up for a few seconds. Two starts in a row that did not get there (a crash while the app comes
/// up, the system killing it for memory) make the next one a safe start: only what the first screen needs is done, the
/// rest waits, and what could be the cause and is cheap to rebuild is reset (the saved window position).
/// </summary>
public static class StartupGuard
{
	private const string PendingKey = "startup.pending";
	private const string FailedKey = "startup.failed";

	/// <summary>How many starts in a row ended before <see cref="Complete"/>.</summary>
	public static int Threshold => 2;

	public static int FailedStarts { get; private set; }

	/// <summary>The next start does the minimum.</summary>
	public static bool IsSafeStart => FailedStarts >= Threshold;

	public static void Begin()
	{
		try
		{
			bool unfinished = Preferences.Default.Get(PendingKey, false);

			FailedStarts =
				unfinished
					? Preferences.Default.Get(FailedKey, 0) + 1
					: 0;

			Preferences.Default.Set(FailedKey, FailedStarts);
			Preferences.Default.Set(PendingKey, true);

			if (IsSafeStart)
			{
				WindowPlacement.Forget();
			}
		}
		catch (Exception ex)
		{
			// Preferences that cannot be read are the very thing this guards against: start normally.
			FailedStarts = 0;
			DiagnosticLog.Write($"[Start] guard unavailable: {ex.Message}");
		}
	}

	/// <summary>The app is up: the next start is a normal one.</summary>
	public static void Complete()
	{
		try
		{
			Preferences.Default.Set(PendingKey, false);
			Preferences.Default.Set(FailedKey, 0);
			FailedStarts = 0;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Start] guard could not be cleared: {ex.Message}");
		}
	}
}
