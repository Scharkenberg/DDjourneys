using DDjourneys.Platforms.Windows.LiveJourney;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DDjourneys.WinUI
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	public partial class App : MauiWinUIApplication
	{
		/// <summary>
		/// Initializes the singleton application object.  This is the first line of authored code
		/// executed, and as such is the logical equivalent of main() or WinMain().
		/// </summary>
		public App()
		{
			WindowsTrace.Write($"Process start: {string.Join(' ', Environment.GetCommandLineArgs())}");

			UnhandledException += (_, e) => WindowsTrace.Write("Unhandled UI exception", e.Exception);

			AppDomain.CurrentDomain.UnhandledException +=
				(_, e) => WindowsTrace.Write("Unhandled exception", e.ExceptionObject as Exception ?? new Exception("unknown"));

			TaskScheduler.UnobservedTaskException +=
				(_, e) => WindowsTrace.Write("Unobserved task exception", e.Exception);

			// Before anything else: a notification click that started this process needs it registered.
			WindowsNotificationHost.Register();

			this.InitializeComponent();
		}

		protected override MauiApp CreateMauiApp()
		{
			try
			{
				MauiApp app = MauiProgram.CreateMauiApp();

				WindowsNotificationHost.Initialize(app.Services);

				return app;
			}
			catch (Exception ex)
			{
				WindowsTrace.Write("CreateMauiApp failed", ex);

				throw;
			}
		}
	}

}
