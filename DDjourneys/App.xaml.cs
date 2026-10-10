using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys;

public partial class App : Application
{
	private readonly Func<AppShell> _shellFactory;
	private readonly AppSettings _settings;
	private bool _uiStarted;

	/// <summary>
	/// Only what every start needs: the process can be created for a widget update or a notification action and never
	/// show a window, so the user interface (the XAML resources, theme, density, the startup guard) is brought up when the
	/// first window is created (<see cref="StartUi"/>), not here.
	/// </summary>
	public App(AppSettings settings)
	{
		_settings = settings;
		_shellFactory = () => new AppShell();

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			DiagnosticLog.Write($"Unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");

		// Last line of defence: log instead of dying on unobserved task faults.
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			DiagnosticLog.Write($"Unobserved task exception: {e.Exception}");
			e.SetObserved();
		};
	}

	/// <summary>
	/// The user interface of the app, once, just before the first window: remembers whether the last start finished (a crash
	/// while starting leads to a safe start; a process that only served a widget never counted), loads the resources and
	/// applies theme and density.
	/// </summary>
	private void StartUi()
	{
		if (_uiStarted)
		{
			return;
		}

		_uiStarted = true;

		StartupGuard.Begin();

		if (StartupGuard.FailedStarts > 0)
		{
			// The last line of the log before this one is the last thing that happened before the process ended.
			DiagnosticLog.Write($"[Start] the previous {StartupGuard.FailedStarts} start(s) did not finish (a crash while starting, or the system ended the app){(StartupGuard.IsSafeStart ? ": safe start" : string.Empty)}");
		}

		InitializeComponent();
		SystemAccessibility.Refresh();
		SystemAccessibility.Changed += (_, _) => Dense.Refresh();
		Motion.Bind(_settings);
		Theme.Initialize(this, _settings);
		Density.Initialize(this, _settings);
	}

	private static DateTimeOffset _lastSystemRefresh;

	/// <summary>The accent colour and the accessibility options (text size, animations) can change in the background.</summary>
	private static void RefreshSystemSettings()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;

		// Resume raises Resumed and Activated together: one pass is enough (the next activation runs it again).
		if (now - _lastSystemRefresh < TimeSpan.FromSeconds(1))
		{
			return;
		}

		_lastSystemRefresh = now;

		Theme.Refresh();
		SystemAccessibility.Refresh();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		StartUi();

		var window = new Window(_shellFactory());

		// The OS accent colour can change while the app is in the background.
		window.Resumed += (_, _) => RefreshSystemSettings();
		window.Activated += (_, _) => RefreshSystemSettings();

		WindowPlacement.Attach(window);

		return window;
	}
}
