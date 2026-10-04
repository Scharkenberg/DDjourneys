using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys;

public partial class App : Application
{
	private readonly Func<AppShell> _shellFactory;

	public App(AppSettings settings)
	{
		InitializeComponent();
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

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(_shellFactory());

		// The OS accent colour can change while the app is in the background.
		window.Resumed += (_, _) => Theme.Refresh();
		window.Activated += (_, _) => Theme.Refresh();

		WindowPlacement.Attach(window);

		return window;
	}
}
