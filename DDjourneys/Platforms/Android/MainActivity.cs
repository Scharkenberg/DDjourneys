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
// Map links (geo:) and the share sheet: "take me there" without knowing anything about this app.
[IntentFilter(
	[Intent.ActionView],
	Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
	DataScheme = "geo")]
[IntentFilter(
	[Intent.ActionSend],
	Categories = [Intent.CategoryDefault],
	DataMimeType = "text/plain")]
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		SystemBars.Apply(this, Support.Theme.IsDark);

		// A cold start from a notification: the request waits until the shell is ready.
		// Not when the activity is recreated (rotation, theme): that intent was handled already.
		if (savedInstanceState is null)
		{
			Accept(Intent);
		}
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

		AndroidShortcuts.Publish(this);

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
			await Task.Delay(400).ConfigureAwait(false);

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
