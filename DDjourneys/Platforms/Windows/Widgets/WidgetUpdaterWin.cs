using System.Collections.Concurrent;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using DDjourneys.Platforms.Windows.LiveJourney;
using DDjourneys.Support;
using DDjourneys.Support.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace DDjourneys.Platforms.Windows.Widgets;

/// <summary>
/// Fetches widget rows and hands the board a card: on demand when a widget becomes visible or is tapped, and in a
/// slow shared cycle while the app runs (the parity of Android's 30-minute update interval). One flight per widget
/// at a time; a failed fetch keeps the old snapshot, marked stale - the same honest degradation as the Android
/// updater. While a widget is on screen a minute tick draws its card again from the cached rows (no network: the
/// countdowns move, departed rows drop out), and asks for new rows when too few are left.
/// </summary>
public static class WidgetUpdaterWin
{
	/// <summary>Android's minimum update interval; the shared cycle of the running app.</summary>
	private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(30);

	private static readonly TimeSpan FetchLimit = TimeSpan.FromSeconds(8);

	/// <summary>The least time between two fetches the minute tick asks for.</summary>
	private static readonly TimeSpan RefillEvery = TimeSpan.FromMinutes(2);

	private static readonly Lock Gate = new();
	private static readonly Dictionary<string, bool> InFlight = new(StringComparer.Ordinal);

	/// <summary>The widgets the board shows right now.</summary>
	private static readonly ConcurrentDictionary<string, bool> Visible = new(StringComparer.Ordinal);

	/// <summary>The card last sent per widget: an unchanged card is not sent again.</summary>
	private static readonly ConcurrentDictionary<string, string> LastSent = new(StringComparer.Ordinal);

	private static readonly ConcurrentDictionary<string, DateTimeOffset> LastFetch = new(StringComparer.Ordinal);

	private static CancellationTokenSource? _cycleSource;

	/// <summary>Starts the shared refresh cycle and the minute tick (called once the app is running).</summary>
	public static void StartCycle()
	{
		CancellationToken token;

		lock (Gate)
		{
			if (_cycleSource is not null)
			{
				return;
			}

			_cycleSource = new CancellationTokenSource();
			token = _cycleSource.Token;
		}

		_ = CycleAsync(token);
		_ = TickAsync(token);
	}

	/// <summary>The board shows the widget (or stops showing it): only shown widgets are drawn again every minute.</summary>
	public static void SetVisible(string id, bool visible)
	{
		if (visible)
		{
			Visible[id] = true;
		}
		else
		{
			Visible.TryRemove(id, out _);
		}
	}

	/// <summary>Forgets what is kept for a widget the board removed.</summary>
	public static void Forget(string id)
	{
		Visible.TryRemove(id, out _);
		LastSent.TryRemove(id, out _);
		LastFetch.TryRemove(id, out _);
	}

	/// <summary>Serves the widget from the cache at once (sent even when unchanged: the board may have lost it); asks for newer rows in the background.</summary>
	public static void Serve(string id)
	{
		WidgetConfig? config = WindowsWidgets.Store?.LoadConfig(id);

		Render(id, config, force: true);

		if (config is { IsComplete: true })
		{
			_ = RefreshAsync(id);
		}
	}

	/// <summary>Fetches the rows for one widget (single flight) and sends the card.</summary>
	public static async Task RefreshAsync(string id)
	{
		if (WindowsWidgets.Store?.LoadConfig(id) is not { IsComplete: true } config)
		{
			RenderSetUp(id, force: true);

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

		LastFetch[id] = DateTimeOffset.UtcNow;

		try
		{
			using var limit = new CancellationTokenSource(FetchLimit);

			// A widget cannot ask for a position in the background: it works with the last fix, like Android's.
			if (WindowsWidgets.Loader is { } loader)
			{
				WidgetSnapshot? snapshot =
					await loader.LoadAsync(
						// The widget shows as many rows as its size has; the row cap of the Android settings does not apply.
						config with { MaxRows = 0 },
						WidgetCard.RowsFor(WindowsWidgets.SizeOf(id)),
						DeviceLocator.LastFix,
						limit.Token)
						.ConfigureAwait(false);

				if (snapshot is not null)
				{
					WindowsWidgets.Store?.SaveSnapshot(id, snapshot);
				}
			}

			Render(id, config, force: false);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Widget {id} refresh failed: {ex.Message}");

			// What was there stays for the next round; this card says so (not persisted, like Android's).
			WidgetSnapshot? previous = WindowsWidgets.Store?.LoadSnapshot(id);

			RenderSnapshot(id, config, previous is null ? null : previous with { IsStale = true }, force: false);
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
	private static void Render(string id, WidgetConfig? config, bool force)
	{
		if (config is null
			|| WindowsWidgets.Store is not { } store)
		{
			RenderSetUp(id, force);

			return;
		}

		RenderSnapshot(id, config, store.LoadSnapshot(id), force);
	}

	/// <summary>One card from a snapshot as it stands (the stale flag included); the set-up card without one.</summary>
	private static void RenderSnapshot(string id, WidgetConfig config, WidgetSnapshot? snapshot, bool force)
	{
		WidgetCardPayload card =
			snapshot is null
				? WidgetCard.SetUp(Strings())
				: WidgetCard.For(
					snapshot,
					WindowsWidgets.SizeOf(id),
					Strings(),
					config.Title,
					WidgetChipImages.For);

		Send(id, card, force);
	}

	private static void RenderSetUp(string id, bool force) =>
		Send(id, WidgetCard.SetUp(Strings()), force);

	private static void Send(string id, WidgetCardPayload card, bool force)
	{
		if (!force
			&& LastSent.TryGetValue(id, out string? last)
			&& string.Equals(last, card.Template, StringComparison.Ordinal))
		{
			return;
		}

		try
		{
			WidgetManager.GetDefault().UpdateWidget(
				new WidgetUpdateRequestOptions(id)
				{
					Template = card.Template,
					Data = card.Data
				});

			LastSent[id] = card.Template;
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

	/// <summary>On the minute, for the widgets on screen: the card again from the cache, and new rows when the cache runs low.</summary>
	private static async Task TickAsync(CancellationToken cancellation)
	{
		while (!cancellation.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(61 - DateTimeOffset.Now.Second), cancellation).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			foreach (string id in Visible.Keys)
			{
				try
				{
					Tick(id);
				}
				catch (Exception ex)
				{
					WindowsTrace.Write($"Drawing widget {id} again failed", ex);
				}
			}
		}
	}

	private static void Tick(string id)
	{
		if (WindowsWidgets.Store is not { } store
			|| store.LoadConfig(id) is not { } config)
		{
			return;
		}

		WidgetSnapshot? snapshot = store.LoadSnapshot(id);

		RenderSnapshot(id, config, snapshot, force: false);

		if (config.IsComplete
			&& snapshot is not null
			&& snapshot.Upcoming(DateTimeOffset.UtcNow).Count < WidgetCard.RowsFor(WindowsWidgets.SizeOf(id))
			&& (!LastFetch.TryGetValue(id, out DateTimeOffset last) || DateTimeOffset.UtcNow - last >= RefillEvery))
		{
			_ = RefreshAsync(id);
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
			widgets.CardSetUpHint,
			widgets.CardInMinutes,
			widgets.CardNow);
	}
}
