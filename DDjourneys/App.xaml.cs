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
		_shellFactory = () => new AppShell();

		// Last line of defence: log instead of dying on unobserved task faults.
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {e.Exception}");
			e.SetObserved();
		};
	}

	protected override Window CreateWindow(IActivationState? activationState) =>
		new Window(_shellFactory());
}
