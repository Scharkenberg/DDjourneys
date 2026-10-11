using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// Opens a rental in the operator's own app. On Android the link goes to that app by its package ("de.nextbike",
/// "com.limebike"): a link the app understands opens the station or the vehicle there, any other starts the app;
/// without the app installed, its store page opens, so the next tap rents. Elsewhere (and when nothing of that
/// works) the operator's web page is the answer. The packages must be named in the manifest's &lt;queries&gt;
/// (Android 11 package visibility), else the app always looks missing.
/// </summary>
public static class RentalApps
{
	public static async Task OpenAsync(Uri? link, string? androidPackage, Uri? web)
	{
		try
		{
#if ANDROID
			if (!string.IsNullOrEmpty(androidPackage)
				&& OpenOnAndroid(link, androidPackage))
			{
				return;
			}
#endif
			Uri? page = link is { Scheme: "http" or "https" } ? link : web;

			if (page is not null)
			{
				await Launcher.Default.OpenAsync(page);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Rental] opening {androidPackage ?? link?.Host ?? "?"} failed: {ex.Message}");

			if (web is not null)
			{
				await Launcher.Default.TryOpenAsync(web);
			}
		}
	}

#if ANDROID
	private static bool OpenOnAndroid(Uri? link, string package)
	{
		Android.Content.Context context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? Android.App.Application.Context;

		if (context.PackageManager is not { } packages)
		{
			return false;
		}

		Android.Content.Intent? launch = packages.GetLaunchIntentForPackage(package);

		if (launch is not null)
		{
			if (link is not null)
			{
				var view = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(link.AbsoluteUri));

				view.SetPackage(package);
				view.AddFlags(Android.Content.ActivityFlags.NewTask);

				if (view.ResolveActivity(packages) is not null)
				{
					context.StartActivity(view);

					return true;
				}
			}

			// The app does not take this link: it opens as it is.
			launch.AddFlags(Android.Content.ActivityFlags.NewTask);
			context.StartActivity(launch);

			return true;
		}

		// Not installed: its store page (Play Store app, else the web store).
		try
		{
			var store = new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse($"market://details?id={package}"));

			store.AddFlags(Android.Content.ActivityFlags.NewTask);
			context.StartActivity(store);

			return true;
		}
		catch (Android.Content.ActivityNotFoundException)
		{
			_ = Launcher.Default.TryOpenAsync(new Uri($"https://play.google.com/store/apps/details?id={package}"));

			return true;
		}
	}
#endif
}
