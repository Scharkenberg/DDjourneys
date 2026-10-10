using DDjourneys.Core.Diagnostics;
using Android.App;
using Android.Content;
using Android.Content.PM;
using DDjourneys.Contract;
using DDjourneys.Core.Contract;
using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Support;
using DDjourneys.Tracking;

namespace DDjourneys.Platforms.Android;

// External contract: ddjourneys:// links and the CONTRACT action (docs/EXTERNAL_CONTRACT.md).
[IntentFilter(
	[Intent.ActionView],
	Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
	DataScheme = ContractVersion.Scheme)]
[IntentFilter(
	[ContractVersion.AndroidAction],
	Categories = [Intent.CategoryDefault])]
// Map links (geo:): "take me there" from any app that shows a place. There is no filter for plain text
// shares: Android matches a share target by MIME type only, never by what the text says, so a text/plain
// filter would put the app into every share sheet on the device.
[IntentFilter(
	[Intent.ActionView],
	Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
	DataScheme = "geo")]
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		// After MAUI's own callback, so it is asked first: back closes the deepest pane while there is one.
		PaneBackCallback.Register(this);

		SystemBars.Apply(this, Support.Theme.IsDark);

		// Whatever handler builds the text fields, none keeps a frame of its own.
		NativeStyling.Watch(this);

		// A cold start from a notification: the request waits until the shell is ready.
		// Not when the activity is recreated (rotation, theme): that intent was handled already.
		if (savedInstanceState is null)
		{
			Accept(Intent);
		}

		WarmUpWebView();
	}

	/// <summary>
	/// The first web view of a process costs a few hundred ms (provider load, renderer start). A throwaway one is
	/// created once the app is idle and dropped again, so the map page finds the engine warm. Never in a safe start,
	/// never where the map is blocked (a failing web view can end the app).
	/// </summary>
	private void WarmUpWebView()
	{
		if (StartupGuard.IsSafeStart || MapSupport.Block != MapBlock.None)
		{
			return;
		}

		global::Android.OS.Looper? looper = global::Android.OS.Looper.MainLooper;
		if (looper is null)
		{
			return;
		}

		var handler = new global::Android.OS.Handler(looper);
		handler.PostDelayed(() =>
		{
			try
			{
				var probe = new global::Android.Webkit.WebView(ApplicationContext!);
				handler.PostDelayed(() =>
				{
					try
					{
						probe.Destroy();
					}
					catch (Exception)
					{
					}
				}, 3000);
			}
			catch (Exception exception)
			{
				DiagnosticLog.Write($"[Start] web view warm-up skipped: {exception.Message}");
			}
		}, 4000);
	}

	public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
	{
		base.OnConfigurationChanged(newConfig);

		// The system switched light/dark (the activity is not recreated): follow it, bars included.
		Support.Theme.Refresh();
		SystemBars.Apply(this, Support.Theme.IsDark);
	}

	protected override void OnNewIntent(Intent? intent)
	{
		base.OnNewIntent(intent);

		Intent = intent;

		Accept(intent);

		ContractEntry.Deliver();
	}

	protected override void OnResume()
	{
		base.OnResume();

		SystemBars.Apply(this, Support.Theme.IsDark);

		if (!StartupGuard.IsSafeStart)
		{
			AndroidShortcuts.Publish(this);
		}

		// Off the UI thread and after the first frame: the tracking graph is built here on a cold start.
		_ = Task.Run(ResumeTrackingAsync);

		if (Service<TrackedJourneyNavigator>() is { } navigator)
		{
			_ = navigator.DeliverAsync();
		}

		ContractEntry.Deliver();
	}

	/// <summary>
	/// A tapped live notification opens its followed journey; a contract link or intent from another
	/// app is queued for the inbox. An intent replayed from the recents list is old news: Android
	/// re-delivers the launching intent then, and it must not repeat.
	/// </summary>
	private static void Accept(Intent? intent)
	{
		if (intent is null
			|| (intent.Flags & ActivityFlags.LaunchedFromHistory) != 0)
		{
			return;
		}

		// Long press on the app icon: "take me home", "departures from here".
		if (intent.Action == AppShortcuts.AndroidAction)
		{
			AppShortcuts.Submit(AppShortcuts.Parse(intent.GetStringExtra(AppShortcuts.ExtraKey)));

			// Consumed: a recreated activity must not run it again.
			intent.SetAction(Intent.ActionMain);

			return;
		}

		if (intent.Action == TrackingActions.Open)
		{
			Service<TrackedJourneyNavigator>()?.Request(
				intent.GetStringExtra(TrackingActions.PlanIdKey));

			return;
		}

		if (ContractIntentReader.Read(intent) is { } request)
		{
			ContractEntry.Submit(request);

			// Consumed: a recreated activity must not run it again.
			intent.SetAction(Intent.ActionMain);
			intent.SetData(null);
		}
	}

	private static T? Service<T>()
		where T : class =>
		IPlatformApplication.Current?.Services.GetService<T>();

	/// <summary>
	/// Reloads the followed journeys and restarts monitoring (the foreground service may have
	/// been stopped by the system while the app was away).
	/// </summary>
	private static async Task ResumeTrackingAsync()
	{
		try
		{
			// After starts that did not finish the first screen comes first: tracking resumes later.
			await Task.Delay(StartupGuard.IsSafeStart ? TimeSpan.FromSeconds(20) : TimeSpan.FromMilliseconds(400)).ConfigureAwait(false);

			// Resolving the tracker creates it (and attaches it to the bridge) on a cold start.
			_ = Service<IJourneyTracker>();

			if (Service<TrackingCallbackBridge>() is { } bridge)
			{
				await bridge.ResumeAsync().ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Journey tracking resume failed: {ex.Message}");
		}
	}
}
