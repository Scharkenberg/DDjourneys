using Android.App;
using Android.Content.PM;
using DDjourneys.Core.Tracking;
using DDjourneys.Platforms.Android;
using DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

namespace DDjourneys
{
	[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
	public class MainActivity : MauiAppCompatActivity
	{
		protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			SystemBars.Apply(this, Support.Theme.IsDark);
		}

		protected override void OnResume()
		{
			base.OnResume();

			SystemBars.Apply(this, Support.Theme.IsDark);

			_ = ResumeTrackingAsync();
		}

		/// <summary>
		/// Reloads the followed journeys and restarts monitoring (the foreground service may have
		/// been stopped by the system while the app was away).
		/// </summary>
		private static async Task ResumeTrackingAsync()
		{
			try
			{
				if (IPlatformApplication.Current?.Services.GetService<IJourneyTracker>()
					is SchutzengelJourneyTracker tracker)
				{
					await tracker.ResumeAsync().ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Journey tracking resume failed: {ex.Message}");
			}
		}
	}
}
