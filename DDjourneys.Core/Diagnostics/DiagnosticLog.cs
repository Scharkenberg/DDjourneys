using System.Globalization;

namespace DDjourneys.Core.Diagnostics;

/// <summary>
/// Opt-in diagnostic log. Nothing is written, anywhere, unless <see cref="Enabled"/> is on (the app switches
/// it with the developer option "Log to file"); then lines go to a text file at <see cref="FilePath"/>.
/// The file is trimmed when it grows past <see cref="MaxBytes"/>. Never throws.
/// </summary>
public static class DiagnosticLog
{
	public const long MaxBytes = 512 * 1024;

	/// <summary>Longest slice of an API body that is logged.</summary>
	public const int MaxBodyChars = 2000;

	private static readonly object Gate = new();

	/// <summary>Full path of the log file; null: nothing is written.</summary>
	public static string? FilePath { get; set; }

	/// <summary>Whether lines are written at all.</summary>
	public static bool Enabled { get; set; }

	public static void Write(string message)
	{
		if (!Enabled || FilePath is not { Length: > 0 } path)
		{
			return;
		}

		try
		{
			string line =
				string.Create(
					CultureInfo.InvariantCulture,
					$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");

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
			// Logging must never disturb the app.
		}
	}

	/// <summary>One external API exchange: who, what, and the (clipped) text that went over the wire.</summary>
	public static void Api(string source, string operation, string? body)
	{
		if (!Enabled)
		{
			return;
		}

		string text =
			(body ?? string.Empty).ReplaceLineEndings(" ");

		if (text.Length > MaxBodyChars)
		{
			text = text[..MaxBodyChars] + $"… ({body!.Length} chars)";
		}

		Write($"[{source}] {operation} {text}".TrimEnd());
	}

	/// <summary>Removes the log file (switching logging off, or the developer options off).</summary>
	public static void Delete()
	{
		if (FilePath is not { Length: > 0 } path)
		{
			return;
		}

		try
		{
			lock (Gate)
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Nothing to clean up that we can reach.
		}
	}

	/// <summary>True when there is a log file with content.</summary>
	public static bool Exists =>
		FilePath is { Length: > 0 } path
		&& File.Exists(path)
		&& new FileInfo(path).Length > 0;
}
