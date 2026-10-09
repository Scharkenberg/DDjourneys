using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys;

public partial class App : Application
{
	private readonly Func<AppShell> _shellFactory;

	public App(AppSettings settings)
	{
		InitializeComponent();
		SystemAccessibility.Refresh();
		SystemAccessibility.Changed += (_, _) => Dense.Refresh();
		Motion.Bind(settings);
		Theme.Initialize(this, settings);
		Density.Initialize(this, settings);
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
		var window = new Window(_shellFactory());

		// The OS accent colour can change while the app is in the background.
		window.Resumed += (_, _) => RefreshSystemSettings();
		window.Activated += (_, _) => RefreshSystemSettings();

		WindowPlacement.Attach(window);

		return window;
	}
}
