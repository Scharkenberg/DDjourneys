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

	private static readonly Lock Gate = new();

	/// <summary>Full path of the log file; null: nothing is written.</summary>
	public static string? FilePath { get; set; }

	/// <summary>Whether lines are written at all. Switching it on writes a first line, so the file exists at once.</summary>
	public static bool Enabled
	{
		get => _enabled;
		set
		{
			bool switchedOn = value && !_enabled;

			_enabled = value;

			if (switchedOn)
			{
				LastError = null;
				Write("[Log] started");
				WriteStartInfo();
			}
		}
	}

	private static bool _enabled;

	/// <summary>
	/// Lines that start every log: build and interface versions (<see cref="InterfaceSchemas"/>), set by the app so a bug
	/// report names what it was made with. Not part of the log while logging is off.
	/// </summary>
	public static Func<string>? StartInfo { get; set; }

	private static void WriteStartInfo()
	{
		try
		{
			foreach (string line in (StartInfo?.Invoke() ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				Write("[Info] " + line.TrimEnd());
			}
		}
		catch (Exception ex)
		{
			Write($"[Info] unavailable: {ex.Message}");
		}
	}

	/// <summary>Why the last write failed (null: it did not); shown next to the log path so a failure is not silent.</summary>
	public static string? LastError { get; private set; }

	/// <summary>
	/// A second place every line goes to while logging is on (the app sends it to the system log under the tag
	/// <c>DDjourneys</c>, so <c>adb logcat -s DDjourneys</c> shows only this app's lines). Never throws.
	/// </summary>
	public static Action<string>? Sink { get; set; }

	public static void Write(string message)
	{
		if (!Enabled)
		{
			return;
		}

		try
		{
			Sink?.Invoke(message);
		}
		catch (Exception)
		{
			// A sink that fails must not stop the file.
		}

		if (FilePath is not { Length: > 0 } path)
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
				if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
				{
					Directory.CreateDirectory(directory);
				}

				if (File.Exists(path)
					&& new FileInfo(path).Length > MaxBytes)
				{
					File.Delete(path);
				}

				File.AppendAllText(path, line + Environment.NewLine);
			}
		}
		catch (Exception ex)
		{
			// Logging must never disturb the app; the reason is kept for the settings page.
			LastError = $"{ex.GetType().Name}: {ex.Message}";
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
