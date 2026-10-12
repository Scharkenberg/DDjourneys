using Android.App;
using Android.Runtime;
using DDjourneys.Platforms.Android.Disruptions;

namespace DDjourneys.Platforms.Android;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override void OnCreate()
	{
		base.OnCreate();

		// B4: the periodic check of the watched lines follows the watch set (the Support hook fires after
		// every toggle). After the base call the app and its services exist - also in a process that the
		// worker or a widget started - so the watch list can be read and the work brought in step.
		WatchedLinesScheduling.Attach();
	}
}
