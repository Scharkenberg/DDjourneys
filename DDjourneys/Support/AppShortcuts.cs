using DDjourneys.Core.Diagnostics;
using DDjourneys.Pages;

namespace DDjourneys.Support;

/// <summary>The quick actions of the app icon (Android long press) and the jump list (Windows).</summary>
public enum AppShortcut
{
	/// <summary>Plan the way from where the device is to the home stop, starting now.</summary>
	Home,

	/// <summary>Departures at the stop nearest to the device.</summary>
	DeparturesHere
}

/// <summary>
/// Where the platforms hand a tapped quick action over, and where it is carried out once the shell exists
/// (a cold start delivers it before the first page is up). The newest request wins.
/// </summary>
public static class AppShortcuts
{
	/// <summary>Intent action (Android) of a quick action; the id travels in <see cref="ExtraKey"/>.</summary>
	public const string AndroidAction = "dev.Scharkenberg.DDjourneys.action.SHORTCUT";

	public const string ExtraKey = "shortcut";

	/// <summary>Launch argument (Windows jump list): <c>shortcut=home</c>.</summary>
	public const string ArgumentKey = "shortcut";

	private const string HomeId = "home";
	private const string DeparturesId = "departures";
	private const int MaxAttempts = 40;

	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);
	private static readonly Lock Gate = new();

	private static AppShortcut? _pending;
	private static bool _delivering;

	public static string IdOf(AppShortcut shortcut) =>
		shortcut == AppShortcut.Home
			? HomeId
			: DeparturesId;

	public static AppShortcut? Parse(string? id) =>
		id?.Trim().ToLowerInvariant() switch
		{
			HomeId => AppShortcut.Home,
			DeparturesId => AppShortcut.DeparturesHere,
			_ => null
		};

	/// <summary>The launch arguments of a jump list entry for <paramref name="shortcut"/>.</summary>
	public static string ArgumentsFor(AppShortcut shortcut) =>
		$"{ArgumentKey}={IdOf(shortcut)}";

	/// <summary>Reads <c>shortcut=home</c> out of a launch argument string (other arguments are ignored).</summary>
	public static AppShortcut? ParseArguments(string? arguments)
	{
		if (string.IsNullOrWhiteSpace(arguments))
		{
			return null;
		}

		foreach (string part in arguments.Split([' ', ';', '&'], StringSplitOptions.RemoveEmptyEntries))
		{
			string text = part.TrimStart('-', '/');

			if (text.StartsWith(ArgumentKey + "=", StringComparison.OrdinalIgnoreCase))
			{
				return Parse(text[(ArgumentKey.Length + 1)..]);
			}
		}

		return null;
	}

	/// <summary>Queues a quick action and carries it out as soon as the shell is ready. Never throws.</summary>
	public static void Submit(AppShortcut? shortcut)
	{
		if (shortcut is null)
		{
			return;
		}

		lock (Gate)
		{
			_pending = shortcut;
		}

		_ = DeliverAsync();
	}

	private static async Task DeliverAsync()
	{
		lock (Gate)
		{
			if (_delivering || _pending is null)
			{
				return;
			}

			_delivering = true;
		}

		try
		{
			while (true)
			{
				AppShortcut next;

				lock (Gate)
				{
					if (_pending is not { } pending)
					{
						return;
					}

					next = pending;
					_pending = null;
				}

				for (int attempt = 0; attempt < MaxAttempts; attempt++)
				{
					bool done = false;

					try
					{
						done = await MainThread.InvokeOnMainThreadAsync(() => TryApplyAsync(next)).ConfigureAwait(false);
					}
					catch (Exception ex)
					{
						DiagnosticLog.Write($"Quick action {next} failed: {ex.Message}");
					}

					if (done)
					{
						break;
					}

					await Task.Delay(RetryDelay).ConfigureAwait(false);
				}
			}
		}
		finally
		{
			lock (Gate)
			{
				_delivering = false;
			}
		}
	}

	/// <summary>Shows the planner or the departures (unwinding pushed pages) and starts the action; false while the shell is not ready.</summary>
	private static async Task<bool> TryApplyAsync(AppShortcut shortcut)
	{
		if (Shell.Current is not { CurrentPage: not null } shell)
		{
			return false;
		}

		await shell.GoToAsync($"//{Routes.Plan}");

		if (shortcut == AppShortcut.Home)
		{
			if (shell.CurrentPage is not PlanPage plan)
			{
				return false;
			}

			_ = plan.TakeMeHomeAsync();

			return true;
		}

		await shell.GoToAsync(Routes.Departures);

		if (shell.CurrentPage is not DeparturesPage departures)
		{
			return false;
		}

		departures.StartHere();

		return true;
	}
}
