using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>
/// Notification-path trace: goes to the shared diagnostic log, and only while the developer option "Log to file" is on.
/// </summary>
internal static class WindowsTrace
{
	/// <summary>
	/// The log must work from the first line of the process: a process the Widgets Board or a notification starts
	/// runs its provider code before MAUI is built, and `MauiProgram` switches the log on only then.
	/// </summary>
	public static void Bootstrap()
	{
		try
		{
			DiagnosticLog.FilePath = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "diagnostics.log");
			DiagnosticLog.Enabled = new DDjourneys.Support.AppSettings().LogToFile;
		}
		catch (Exception)
		{
			// Without a path or settings the log stays off; nothing else depends on it.
		}
	}

	public static void Write(string message) =>
		DiagnosticLog.Write($"[TRACKING] [{Environment.ProcessId}] {message}");

	public static void Write(string message, Exception exception) =>
		Write($"{message}: {exception}");
}
