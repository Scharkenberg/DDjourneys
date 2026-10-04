using System.Globalization;

namespace DDjourneys.Core.Diagnostics;

/// <summary>
/// Minimal diagnostic log: always to the debug output, and to a text file once <see cref="FilePath"/> is set.
/// The file is trimmed when it grows past <see cref="MaxBytes"/>. Never throws.
/// </summary>
public static class DiagnosticLog
{
	public const long MaxBytes = 256 * 1024;

	private static readonly object Gate = new();

	/// <summary>Full path of the log file; null: debug output only.</summary>
	public static string? FilePath { get; set; }

	public static void Write(string message)
	{
		string line =
			string.Create(
				CultureInfo.InvariantCulture,
				$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");

		System.Diagnostics.Debug.WriteLine(line);

		if (FilePath is not { Length: > 0 } path)
		{
			return;
		}

		try
		{
			lock (Gate)
			{
				if (File.Exists(path)
					&& new FileInfo(path).Length > MaxBytes)
				{
					File.Delete(path);
				}

				File.AppendAllText(path, line + Environment.NewLine);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			System.Diagnostics.Debug.WriteLine($"Diagnostic log write failed: {ex.Message}");
		}
	}
}
