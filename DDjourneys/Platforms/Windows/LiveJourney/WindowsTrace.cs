using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>
/// Notification-path trace: goes to the shared diagnostic log, and only while the developer option "Log to file" is on.
/// </summary>
internal static class WindowsTrace
{
	public static void Write(string message) =>
		DiagnosticLog.Write($"[TRACKING] [{Environment.ProcessId}] {message}");

	public static void Write(string message, Exception exception) =>
		Write($"{message}: {exception}");
}
