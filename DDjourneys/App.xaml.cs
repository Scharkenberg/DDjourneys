using DDjourneys.Support;

namespace DDjourneys;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		Theme.Initialize(this);
	}

	protected override Window CreateWindow(IActivationState? activationState) =>
		new Window(new AppShell());
}
