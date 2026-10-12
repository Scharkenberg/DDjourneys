using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;
using AndroidX.Work;

namespace DDjourneys.Platforms.Android.Disruptions;

/// <summary>
/// Schedules and cancels the periodic check of the watched lines: unique periodic work, one run per
/// quarter of an hour (WorkManager's minimum) while the device is connected. Re-scheduling keeps the
/// running schedule (<c>Keep</c>: the interval never changes, and the next run must not be pushed out by
/// every app start). Called once per process (<see cref="Attach"/>, after the MAUI app exists) and after
/// every change of the watch set, so the check runs exactly while at least one line is watched.
/// </summary>
internal static class WatchedLinesScheduling
{
	internal const string UniqueName = "dd.watchedlines.check";

	/// <summary>WorkManager's minimum periodic interval.</summary>
	private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

	private static bool _attached;

	/// <summary>Subscribes to the watch list (the Support hook, once per process) and syncs right away.</summary>
	public static void Attach()
	{
		if (_attached)
		{
			return;
		}

		_attached = true;

		WatchedLines.Changed += (_, _) => Sync();
		Sync();
	}

	/// <summary>Schedules the check while the list is non-empty and cancels it when it is empty.</summary>
	public static void Sync()
	{
		try
		{
			WorkManager manager = WorkManager.GetInstance(global::Android.App.Application.Context);

			if (WatchedLines.Load().Count == 0)
			{
				manager.CancelUniqueWork(UniqueName);

				return;
			}

			PeriodicWorkRequest request =
				new PeriodicWorkRequest.Builder(typeof(WatchedLinesWorker), Interval)
					.SetConstraints(
						new Constraints.Builder()
							.SetRequiredNetworkType(NetworkType.Connected!)
							.Build())
					.Build();

			manager.EnqueueUniquePeriodicWork(UniqueName, ExistingPeriodicWorkPolicy.Keep!, request);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Scheduling the check of the watched lines failed: {ex.Message}");
		}
	}
}
