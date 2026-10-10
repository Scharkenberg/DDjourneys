using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Support;
using DDjourneys.Support.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DDjourneys.Platforms.Windows.Widgets;

/// <summary>
/// Fetches widget rows and hands the board a card: on demand when a widget becomes visible or its refresh
/// button is used, and in a slow shared cycle while the app runs (the parity of Android's 30-minute update
/// interval). One flight per widget at a time; a failed fetch keeps the old snapshot, marked stale - the
/// same honest degradation as the Android updater.
/// </summary>
public static class WidgetUpdaterWin
{
	/// <summary>Android's minimum update interval; the shared cycle of the running app.</summary>
	private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(30);

	private static readonly TimeSpan FetchLimit = TimeSpan.FromSeconds(8);

	private static readonly Lock Gate = new();
	private static readonly Dictionary<string, bool> InFlight = new(StringComparer.Ordinal);
	private static CancellationTokenSource? _cycleSource;

	/// <summary>Starts the shared refresh cycle (called once the app is running).</summary>
	public static void StartCycle()
	{
		lock (Gate)
		{
			if (_cycleSource is not null)
			{
				return;
			}

			_cycleSource = new CancellationTokenSource();
		}

		_ = CycleAsync(_cycleSource.Token);
	}

	/// <summary>Serves the widget from the cache at once; asks for newer rows in the background.</summary>
	public static void Serve(string id)
	{
		RenderFromCache(id);

		if (WindowsWidgets.Store?.LoadConfig(id) is { IsComplete: true })
		{
			_ = RefreshAsync(id);
		}
	}

	/// <summary>Fetches the rows for one widget (single flight) and sends the card.</summary>
	public static async Task RefreshAsync(string id)
	{
		if (WindowsWidgets.Store?.LoadConfig(id) is not { IsComplete: true } config)
		{
			RenderSetUp(id);

			return;
		}

		lock (Gate)
		{
			if (InFlight.TryGetValue(id, out bool busy) && busy)
			{
				return;
			}

			InFlight[id] = true;
		}

		try
		{
			using var limit = new CancellationTokenSource(FetchLimit);

			// A widget cannot ask for a position in the background: it works with the last fix, like Android's.
			if (WindowsWidgets.Loader is { } loader)
			{
				WidgetSnapshot? snapshot =
						await loader.LoadAsync(
							config,
							WidgetCard.RowsFor(WindowsWidgets.SizeOf(id), config.MaxRows),
							DeviceLocator.LastFix,
							limit.Token)
							.ConfigureAwait(false);

				if (snapshot is not null)
				{
					WindowsWidgets.Store?.SaveSnapshot(id, snapshot);
				}
			}

			RenderFromCache(id);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Widget {id} refresh failed: {ex.Message}");

			// What was there stays for the next round; this card says so (not persisted, like Android's).
			WidgetSnapshot? previous = WindowsWidgets.Store?.LoadSnapshot(id);

			RenderSnapshot(id, previous is null ? null : previous with { IsStale = true });
		}
		finally
		{
			lock (Gate)
			{
				InFlight[id] = false;
			}
		}
	}

	/// <summary>The card for the cached snapshot, whatever it says; the set-up card when there are no settings.</summary>
	private static void RenderFromCache(string id)
	{
		if (WindowsWidgets.Store is not { } store)
		{
			return;
		}

		if (store.LoadConfig(id) is not { } config)
		{
			RenderSetUp(id);

			return;
		}

		RenderSnapshot(id, store.LoadSnapshot(id));
	}

	/// <summary>One card from a snapshot as it stands (the stale flag included); the set-up card without one.</summary>
	private static void RenderSnapshot(string id, WidgetSnapshot? snapshot)
	{
		if (WindowsWidgets.Store?.LoadConfig(id) is not { } config)
		{
			RenderSetUp(id);

			return;
		}

		WidgetCardPayload card =
			snapshot is null
				? WidgetCard.SetUp(Strings())
				: WidgetCard.For(
					snapshot,
					WindowsWidgets.SizeOf(id),
					config.MaxRows,
					Strings(),
					config.Title,
					WidgetChipImages.For);

		Send(id, card);
	}

	private static void RenderSetUp(string id) =>
		Send(id, WidgetCard.SetUp(Strings()));

	private static void Send(string id, WidgetCardPayload card)
	{
		try
		{
			WidgetManager.GetDefault().UpdateWidget(
				new WidgetUpdateRequestOptions(id)
				{
					Template = card.Template,
					Data = card.Data
				});
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Updating the widget failed", ex);
		}
	}

	private static async Task CycleAsync(CancellationToken cancellation)
	{
		while (!cancellation.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(Cycle, cancellation).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			string[] pinned = WindowsWidgets.PinnedIds();

			// One shared loop, round-robin: not five timers for five widgets.
			foreach (string id in pinned)
			{
				await RefreshAsync(id).ConfigureAwait(false);

				try
				{
					await Task.Delay(TimeSpan.FromSeconds(1), cancellation).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}
	}

	private static WidgetCardStrings Strings()
	{
		WidgetStrings widgets = LocalizationService.Current.CurrentStrings.Widgets;

		return new WidgetCardStrings(
			widgets.UpdatedAt,
			widgets.CardRefresh,
			widgets.CardOpen,
			widgets.CardSetUp,
			widgets.RefreshFailed,
			widgets.CardSetUpHint);
	}
}
