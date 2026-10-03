using System.Text;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>
/// Log file for the notification path, readable after the app was closed or crashed (no debugger needed):
/// %LOCALAPPDATA%\Packages\&lt;package family name&gt;\LocalCache\Local\DDjourneys\tracking.log
/// </summary>
internal static class WindowsTrace
{
	private const long MaxBytes = 256 * 1024;

	private static readonly Lock Gate = new();

	private static readonly string PathName =
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"DDjourneys",
			"tracking.log");

	public static void Write(string message)
	{
		System.Diagnostics.Debug.WriteLine($"[TRACKING] {message}");

		try
		{
			lock (Gate)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);

				if (File.Exists(PathName) && new FileInfo(PathName).Length > MaxBytes)
				{
					File.Delete(PathName);
				}

				File.AppendAllText(
					PathName,
					$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}",
					Encoding.UTF8);
			}
		}
		catch
		{
			// Logging must never take the app down.
		}
	}

	public static void Write(string message, Exception exception) =>
		Write($"{message}: {exception}");
}
