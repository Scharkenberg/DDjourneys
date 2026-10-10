using DDjourneys.Platforms.Windows;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Platforms.Windows.Widgets;
using Microsoft.Extensions.DependencyInjection;
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
			WindowsTrace.Bootstrap();

			WindowsTrace.Write($"Process start: {string.Join(' ', Environment.GetCommandLineArgs())}");

			UnhandledException += (_, e) => WindowsTrace.Write("Unhandled UI exception", e.Exception);

			AppDomain.CurrentDomain.UnhandledException +=
				(_, e) => WindowsTrace.Write("Unhandled exception", e.ExceptionObject as Exception ?? new Exception("unknown"));

			TaskScheduler.UnobservedTaskException +=
				(_, e) => WindowsTrace.Write("Unobserved task exception", e.Exception);

			// Before anything else: a notification click that started this process needs it registered.
			WindowsNotificationHost.Register();
			WindowsBackground.Initialize(WindowsNotificationHost.Embedded);

			// The Widgets Board waits for this class object after it started the app: registered now, not
			// after the MAUI app has been built (that takes seconds).
			WindowsWidgets.RegisterClassObject();

			this.InitializeComponent();

			// WinUI's own opaque page/navigation backgrounds must not hide the window material.
			WindowsMaterial.Prepare();
		}

		protected override MauiApp CreateMauiApp()
		{
			try
			{
				MauiApp app = MauiProgram.CreateMauiApp();

				// Every MAUI window: closing hides it while a journey is monitored (see WindowsBackground).
				Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(
					"DDjourneysBackground",
					(handler, _) =>
					{
						if (handler.PlatformView is Microsoft.UI.Xaml.Window window)
						{
							WindowsBackground.Attach(window);
							WindowsVisibility.Attach(window);
							WindowsMaterial.Attach(window);
						}
					});

				WindowsNotificationHost.Initialize(app.Services);

				// The Widgets Board: the class object it CoCreates, and the store and loader behind it.
				WindowsWidgets.Initialize(
					app.Services.GetRequiredService<Support.Widgets.IWidgetStore>(),
					app.Services.GetRequiredService<Support.Widgets.WidgetLoader>());

				_ = WindowsJumpList.UpdateAsync();

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
