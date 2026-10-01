using Android.App;
using Android.Content.PM;
using Android.OS;
using DDjourneys.Core.Tracking;
using DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

namespace DDjourneys
{
	[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
	public class MainActivity : MauiAppCompatActivity
	{
		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			_ = ResumeTrackingAsync();
		}

		private async Task ResumeTrackingAsync()
		{
			try
			{
				var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
				if (services?.GetService<IJourneyTracker>() is SchutzengelJourneyTracker tracker)
					await tracker.ResumeStoredAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Journey tracking recovery failed: {ex.Message}");
			}
		}
	}
}
