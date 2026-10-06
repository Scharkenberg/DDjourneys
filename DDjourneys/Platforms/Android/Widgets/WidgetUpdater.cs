using System.Collections.Concurrent;
using Android.Appwidget;
using Android.Content;
using Android.Content.Res;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using DDjourneys.Support.Widgets;

namespace DDjourneys.Platforms.Android.Widgets;

internal enum WidgetUpdateReason
{
	/// <summary>The system's periodic update: fetches only when the widget's own interval is over.</summary>
	System,

	/// <summary>A tap on the widget surface.</summary>
	Manual,

	/// <summary>The settings were just saved.</summary>
	Configured,

	/// <summary>The widget was resized: drawn again from what it already has.</summary>
	Options
}

/// <summary>Draws the widgets and refreshes their data when it is due.</summary>
internal static class WidgetUpdater
{
	/// <summary>A broadcast receiver may run for about ten seconds; the fetch has to end before.</summary>
	private static readonly TimeSpan FetchLimit = TimeSpan.FromSeconds(8);

	private static readonly ConcurrentDictionary<int, SemaphoreSlim> Gates = new();

	public static async Task UpdateAsync(Context context, IReadOnlyCollection<int> ids, WidgetUpdateReason reason)
	{
		foreach (int id in ids)
		{
			try
			{
				await UpdateOneAsync(context, id, reason).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write($"Widget {id} update failed: {ex}");
			}
		}
	}

	private static async Task UpdateOneAsync(Context context, int id, WidgetUpdateReason reason)
	{
		AppWidgetManager manager = AppWidgetManager.GetInstance(context)!;

		WidgetConfig? config = WidgetStore.LoadConfig(id);
		WidgetKind? kind = config?.Kind ?? WidgetNames.KindOf(manager.GetAppWidgetInfo(id)?.Provider?.ClassName);

		if (kind is not { } known)
		{
			return;
		}

		(double width, double height) = Size(context, manager, id);

		WidgetSnapshot? snapshot = WidgetStore.LoadSnapshot(id);

		if (config is null || !config.IsComplete)
		{
			manager.UpdateAppWidget(id, WidgetRenderer.Render(context, id, known, config, null, needsSetup: true, refreshing: false, width, height));

			return;
		}

		int fitting = WidgetLayout.For(width, height, config.MaxRows, context.Resources?.Configuration?.FontScale ?? 1).Rows;

		if (!ShouldFetch(config, id, snapshot, reason, fitting))
		{
			manager.UpdateAppWidget(id, WidgetRenderer.Render(context, id, known, config, snapshot, needsSetup: false, refreshing: false, width, height));

			return;
		}

		SemaphoreSlim gate = Gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

		// A second refresh of the same widget while one is running would only repeat the same requests.
		if (!await gate.WaitAsync(0).ConfigureAwait(false))
		{
			return;
		}

		try
		{
			manager.UpdateAppWidget(id, WidgetRenderer.Render(context, id, known, config, snapshot, needsSetup: false, refreshing: true, width, height));

			snapshot = await FetchAsync(context, id, config, snapshot, fitting).ConfigureAwait(false);

			manager.UpdateAppWidget(id, WidgetRenderer.Render(context, id, known, config, snapshot, needsSetup: false, refreshing: false, width, height));
		}
		finally
		{
			gate.Release();
		}
	}

	private static bool ShouldFetch(WidgetConfig config, int id, WidgetSnapshot? snapshot, WidgetUpdateReason reason, int fitting) =>
		reason switch
		{
			// Made taller than the last refresh asked for ("as many as fit"): the rows that now fit are fetched, but not
			// again and again while the launcher reports sizes.
			WidgetUpdateReason.Options =>
				config.MaxRows == 0
				&& snapshot is { IsStale: false, Message.Length: 0 }
				&& fitting > snapshot.Requested
				&& snapshot.Rows.Count >= snapshot.Requested
				&& (WidgetStore.LastFetched(id) is not { } last || DateTimeOffset.UtcNow - last > TimeSpan.FromSeconds(45)),
			WidgetUpdateReason.Manual or WidgetUpdateReason.Configured => true,
			WidgetUpdateReason.System =>
				config.AutoRefresh
				&& (snapshot is null
					|| WidgetStore.LastFetched(id) is not { } fetched
					|| DateTimeOffset.UtcNow - fetched >= TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes - 2))),
			_ => false
		};

	private static async Task<WidgetSnapshot> FetchAsync(Context context, int id, WidgetConfig config, WidgetSnapshot? previous, int fitting)
	{
		WidgetStrings strings = LocalizationService.Current.CurrentStrings.Widgets;

		try
		{
			WidgetLoader loader =
				IPlatformApplication.Current?.Services.GetService<WidgetLoader>()
				?? throw new InvalidOperationException("The widget loader is not available.");

			using var limit = new CancellationTokenSource(FetchLimit);

			WidgetSnapshot fresh =
				await loader
					.LoadAsync(config, fitting, () => AndroidWidgetLocation.Get(context), limit.Token)
					.ConfigureAwait(false);

			WidgetStore.SaveSnapshot(id, fresh);

			return fresh;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Widget {id} refresh failed: {ex.Message}");

			// What was there stays, marked as old; a widget that never had data says so.
			return previous is null
				? new WidgetSnapshot { IsStale = true, Message = strings.RefreshFailed }
				: previous with { IsStale = true };
		}
	}

	/// <summary>The size the launcher gives the widget, in dp: width by height for the current orientation.</summary>
	private static (double Width, double Height) Size(Context context, AppWidgetManager manager, int id)
	{
		global::Android.OS.Bundle? options = manager.GetAppWidgetOptions(id);

		bool portrait = context.Resources?.Configuration?.Orientation == Orientation.Portrait;

		double width = options?.GetInt(portrait ? AppWidgetManager.OptionAppwidgetMinWidth : AppWidgetManager.OptionAppwidgetMaxWidth) ?? 0;
		double height = options?.GetInt(portrait ? AppWidgetManager.OptionAppwidgetMaxHeight : AppWidgetManager.OptionAppwidgetMinHeight) ?? 0;

		return (width > 0 ? width : 160, height > 0 ? height : 160);
	}
}
