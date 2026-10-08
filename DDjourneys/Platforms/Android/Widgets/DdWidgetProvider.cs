using Android.App;
using Android.Appwidget;
using Android.Content;
using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>
/// Common behaviour of the five widgets. The system calls <see cref="OnUpdate"/> on its own schedule (30 minutes
/// at the shortest); a tap on the surface arrives as <see cref="WidgetNames.Refresh"/>. The work is asynchronous,
/// so the receiver is kept alive (<c>goAsync</c>) until it is done.
/// </summary>
public abstract class DdWidgetProvider : AppWidgetProvider
{
	public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds) =>
		Run(context, appWidgetIds, WidgetUpdateReason.System);

	public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, global::Android.OS.Bundle? newOptions) =>
		Run(context, [appWidgetId], WidgetUpdateReason.Options);

	public override void OnDeleted(Context? context, int[]? appWidgetIds)
	{
		foreach (int id in appWidgetIds ?? [])
		{
			WidgetStore.Remove(id);
		}
	}

	public override void OnReceive(Context? context, Intent? intent)
	{
		if (intent?.Action == WidgetNames.Refresh)
		{
			int id = intent.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId);

			if (id != AppWidgetManager.InvalidAppwidgetId)
			{
				Run(context, [id], WidgetUpdateReason.Manual);
			}

			return;
		}

		base.OnReceive(context, intent);
	}

	private void Run(Context? context, int[]? ids, WidgetUpdateReason reason)
	{
		if (context is null || ids is null || ids.Length == 0)
		{
			return;
		}

		PendingResult? pending = GoAsync();

		_ = Task.Run(
			async () =>
			{
				try
				{
					await WidgetUpdater.UpdateAsync(context.ApplicationContext ?? context, ids, reason).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					DiagnosticLog.Write($"Widget update failed: {ex}");
				}
				finally
				{
					pending?.Finish();
				}
			});
	}
}

[BroadcastReceiver(Name = WidgetNames.Route, Label = "@string/widget_route_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_route_info")]
public sealed class RouteWidgetProvider : DdWidgetProvider
{
}

[BroadcastReceiver(Name = WidgetNames.Departures, Label = "@string/widget_departures_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_departures_info")]
public sealed class DeparturesWidgetProvider : DdWidgetProvider
{
}

[BroadcastReceiver(Name = WidgetNames.Arrivals, Label = "@string/widget_arrivals_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_arrivals_info")]
public sealed class ArrivalsWidgetProvider : DdWidgetProvider
{
}

[BroadcastReceiver(Name = WidgetNames.Nearby, Label = "@string/widget_nearby_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_nearby_info")]
public sealed class NearbyWidgetProvider : DdWidgetProvider
{
}

[BroadcastReceiver(Name = WidgetNames.NearbyDepartures, Label = "@string/widget_nearby_departures_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_nearby_departures_info")]
public sealed class NearbyDeparturesWidgetProvider : DdWidgetProvider
{
}
