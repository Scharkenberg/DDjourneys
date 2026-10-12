using System.Globalization;
using Android.Content;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;
using AndroidX.Work;
using WorkResult = AndroidX.Work.ListenableWorker.Result;

namespace DDjourneys.Platforms.Android.Disruptions;

/// <summary>
/// The periodic background check of the watched lines: per provider the same check the app makes
/// (<see cref="WatchedLines.CheckAsync"/>, through the provider scope the widgets use, on the check's own
/// gate); nothing is fetched for an empty watch set, and every changed fingerprint becomes one
/// notification (<see cref="WatchedLineNotification"/>, only the changed lines - the first sight of a line
/// is its baseline, not news). Failures propagate, so WorkManager retries with its own backoff. Scheduled
/// and cancelled by <see cref="WatchedLinesScheduling"/>, which also drops the work when the list is empty.
/// </summary>
internal sealed class WatchedLinesWorker(
	Context context,
	WorkerParameters parameters) : Worker(context, parameters)
{
	public override WorkResult DoWork()
	{
		if (IsStopped)
		{
			return WorkResult.Success();
		}

		try
		{
			CheckAsync().GetAwaiter().GetResult();

			return WorkResult.Success();
		}
		catch (OperationCanceledException)
		{
			return WorkResult.Retry();
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Checking the watched lines in the background failed: {ex.Message}");

			return WorkResult.Retry();
		}
	}

	private static async Task CheckAsync()
	{
		// The worker runs in the app's process; the services have existed since the process start (the widget path).
		IServiceProvider services =
			IPlatformApplication.Current?.Services
				?? throw new InvalidOperationException("The app services are not available.");

		NetworkService? network = services.GetService<NetworkService>();
		ProviderRegistry? providers = services.GetService<ProviderRegistry>();

		if (network is null || providers is null)
		{
			throw new InvalidOperationException("The network service is not available.");
		}

		WatchedLineStrings strings = LocalizationService.Current.CurrentStrings.WatchedLines;

		List<string> changedLines = [];

		foreach (string providerId in WatchedLines.Load()
			.Select(line => line.ProviderId)
			.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (providers.Find(providerId) is null)
			{
				// Lines of a provider the app no longer knows cannot be checked; the states of other providers stay.
				DiagnosticLog.Write($"Watched lines of an unknown provider are not checked: {providerId}");

				continue;
			}

			// The provider scope of the widgets: the fetch answers through the provider the lines belong to,
			// whichever one the app shows right now.
			using (providers.Override(providerId))
			{
				WatchedLineCheckResult result =
					await WatchedLines.CheckAsync(network, providerId, []).ConfigureAwait(false);

				changedLines.AddRange(
					result.Changed
						.Where(change => change.Kind == LineDisruptionChangeKind.Changed)
						.Select(change => change.LineName));
			}
		}

		if (changedLines.Count == 0)
		{
			return;
		}

		WatchedLineNotification.Show(
			changedLines.Count == 1
				? string.Format(CultureInfo.CurrentCulture, strings.NotifChangedSingle, changedLines[0])
				: string.Format(
					CultureInfo.CurrentCulture,
					strings.NotifChangedMany,
					changedLines.Count,
					string.Join(", ", changedLines)));
	}
}
