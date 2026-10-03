using Android.App;
using Android.Content;
using Android.Content.PM;
using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Platforms.Android;
using DDjourneys.Tracking;

namespace DDjourneys
{
	[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
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

		protected override void OnNewIntent(Intent? intent)
		{
			base.OnNewIntent(intent);

			Intent = intent;

			Accept(intent);
		}

		protected override void OnResume()
		{
			base.OnResume();

			SystemBars.Apply(this, Support.Theme.IsDark);

			_ = ResumeTrackingAsync();

			if (Service<TrackedJourneyNavigator>() is { } navigator)
			{
				_ = navigator.DeliverAsync();
			}
		}

		/// <summary>A tapped live notification or alert opens its followed journey.</summary>
		private static void Accept(Intent? intent)
		{
			if (intent is null || intent.Action != TrackingActions.Open)
			{
				return;
			}

			Service<TrackedJourneyNavigator>()?.Request(
				intent.GetStringExtra(TrackingActions.PlanIdKey));
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
				// Resolving the tracker creates it (and attaches it to the bridge) on a cold start.
				_ = Service<IJourneyTracker>();

				if (Service<TrackingCallbackBridge>() is { } bridge)
				{
					await bridge.ResumeAsync().ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Journey tracking resume failed: {ex.Message}");
			}
		}
	}
}
