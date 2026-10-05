using System.Globalization;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Views;
using Android.Widget;
using DDjourneys.Core.Models;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>Draws a widget: <see cref="RemoteViews"/> of the one layout, filled as far as the size allows.</summary>
internal static class WidgetRenderer
{
	private const int OpenCode = 1_000_000;
	private const int ConfigureCode = 2_000_000;

	public static RemoteViews Render(
		Context context,
		int widgetId,
		WidgetKind kind,
		WidgetConfig? config,
		WidgetSnapshot? snapshot,
		bool needsSetup,
		bool refreshing,
		double widthDp,
		double heightDp)
	{
		WidgetStrings strings = LocalizationService.Current.CurrentStrings.Widgets;

		var views = new RemoteViews(context.PackageName, WidgetResources.Layout(context, "widget_main"));

		int root = WidgetResources.Id(context, "w_root");

		// The whole surface refreshes; before the widget is set up it opens its settings instead.
		views.SetOnClickPendingIntent(root, needsSetup ? ConfigureIntent(context, widgetId) : RefreshIntent(context, widgetId, kind));
		views.SetOnClickPendingIntent(WidgetResources.Id(context, "w_open"), OpenAppIntent(context, widgetId));
		views.SetInt(WidgetResources.Id(context, "w_open"), "setColorFilter", WidgetResources.ColorValue(context, "widget_accent"));

		WidgetLayout layout = WidgetLayout.For(widthDp, heightDp, config?.MaxRows ?? 0);

		views.SetTextViewText(WidgetResources.Id(context, "w_title"), Title(kind, snapshot, strings));

		Header(context, views, layout, snapshot, refreshing, strings);

		IReadOnlyList<WidgetRow> rows =
			needsSetup || snapshot is null
				? []
				: Fit(snapshot.Upcoming(DateTimeOffset.UtcNow), layout.Rows);

		string message =
			needsSetup
				? strings.SetUp
				: snapshot is null
					? strings.Loading
					: rows.Count == 0
						? snapshot.Message.Length > 0
							? snapshot.Message
							: snapshot.IsStale
								? strings.RefreshFailed
								: string.Empty
						: string.Empty;

		int messageId = WidgetResources.Id(context, "w_message");

		views.SetTextViewText(messageId, message);
		views.SetViewVisibility(messageId, message.Length > 0 ? ViewStates.Visible : ViewStates.Gone);

		for (int slot = 0; slot < WidgetLayout.MaxRows; slot++)
		{
			Row(context, views, slot, slot < rows.Count ? rows[slot] : null, layout.Detail);
		}

		return views;
	}

	/// <summary>The rows that fit; a stop header left without its departures is not shown.</summary>
	private static IReadOnlyList<WidgetRow> Fit(IReadOnlyList<WidgetRow> rows, int limit)
	{
		List<WidgetRow> fitted = [.. rows.Take(limit)];

		while (fitted.Count > 0 && fitted[^1].Kind == WidgetRowKind.Header)
		{
			fitted.RemoveAt(fitted.Count - 1);
		}

		return fitted;
	}

	private static string Title(WidgetKind kind, WidgetSnapshot? snapshot, WidgetStrings strings) =>
		snapshot is { Title.Length: > 0 }
			? snapshot.Title
			: kind switch
			{
				WidgetKind.Route => strings.NameRoute,
				WidgetKind.Departures => strings.NameDepartures,
				WidgetKind.Arrivals => strings.NameArrivals,
				WidgetKind.NearbyStops => strings.NameNearby,
				_ => strings.NameNearbyDepartures
			};

	private static void Header(
		Context context,
		RemoteViews views,
		WidgetLayout layout,
		WidgetSnapshot? snapshot,
		bool refreshing,
		WidgetStrings strings)
	{
		int updated = WidgetResources.Id(context, "w_updated");

		string text = string.Empty;
		string color = "widget_muted";

		if (refreshing)
		{
			text = "…";
		}
		else if (snapshot is { IsStale: true })
		{
			text = strings.RefreshFailed;
			color = "widget_late";
		}
		else if (layout.ShowUpdated && snapshot?.UpdatedAt is { } at)
		{
			text = string.Format(CultureInfo.CurrentCulture, strings.UpdatedAt, Format.Time(at));
		}

		views.SetTextViewText(updated, text);
		views.SetTextColor(updated, WidgetResources.ColorOf(context, color));
		views.SetViewVisibility(updated, text.Length > 0 ? ViewStates.Visible : ViewStates.Gone);
	}

	private static void Row(Context context, RemoteViews views, int slot, WidgetRow? row, WidgetDetail detail)
	{
		int container = WidgetResources.Id(context, $"w_row_{slot}");

		if (row is null)
		{
			views.SetViewVisibility(container, ViewStates.Gone);

			return;
		}

		views.SetViewVisibility(container, ViewStates.Visible);

		int chip = WidgetResources.Id(context, $"w_chip_{slot}");
		int main = WidgetResources.Id(context, $"w_main_{slot}");
		int sub = WidgetResources.Id(context, $"w_sub_{slot}");
		int delay = WidgetResources.Id(context, $"w_delay_{slot}");
		int time = WidgetResources.Id(context, $"w_time_{slot}");

		bool header = row.Kind == WidgetRowKind.Header;

		// Chip: the line (or the distance of a stop), coloured by mode.
		bool showChip = !header && row.Chip.Length > 0;

		views.SetViewVisibility(chip, showChip ? ViewStates.Visible : ViewStates.Gone);

		if (showChip)
		{
			views.SetTextViewText(chip, row.Chip);
			views.SetInt(chip, "setBackgroundResource", WidgetResources.Drawable(context, ChipDrawable(row.Mode)));
		}

		// Main text: a narrow widget keeps chip and time; a row without a time (a stop) keeps its name.
		bool showMain = row.Main.Length > 0 && (detail != WidgetDetail.Minimal || row.Time.Length == 0 || header);

		views.SetViewVisibility(main, showMain ? ViewStates.Visible : ViewStates.Gone);

		if (showMain)
		{
			views.SetTextViewText(main, row.Main);
			views.SetTextColor(main, WidgetResources.ColorOf(context, header ? "widget_accent" : "widget_ink"));
		}

		bool showSub = detail == WidgetDetail.Full && row.Sub.Length > 0;

		views.SetViewVisibility(sub, showSub ? ViewStates.Visible : ViewStates.Gone);

		if (showSub)
		{
			views.SetTextViewText(sub, row.Sub);
		}

		bool showDelay = detail == WidgetDetail.Full && row.Delay.Length > 0;

		views.SetViewVisibility(delay, showDelay ? ViewStates.Visible : ViewStates.Gone);

		if (showDelay)
		{
			views.SetTextViewText(delay, row.Delay);
			views.SetTextColor(delay, WidgetResources.ColorOf(context, row.DelayLevel == WidgetDelay.Cancelled ? "widget_cancelled" : "widget_late"));
		}

		views.SetViewVisibility(time, row.Time.Length > 0 ? ViewStates.Visible : ViewStates.Gone);
		views.SetTextViewText(time, row.Time);

		// A late or cancelled departure shows it in the time too, so even the smallest widget says so.
		views.SetTextColor(
			time,
			WidgetResources.ColorOf(
				context,
				header
					? "widget_muted"
					: row.DelayLevel switch
					{
						WidgetDelay.Cancelled => "widget_cancelled",
						WidgetDelay.Late => "widget_late",
						_ => "widget_ink"
					}));
	}

	private static string ChipDrawable(TransitMode mode) =>
		mode switch
		{
			TransitMode.Tram => "widget_chip_tram",
			TransitMode.Bus => "widget_chip_bus",
			TransitMode.SuburbanRail => "widget_chip_suburban",
			TransitMode.Subway or TransitMode.RegionalTrain or TransitMode.LongDistanceTrain => "widget_chip_train",
			TransitMode.Ferry => "widget_chip_ferry",
			TransitMode.CableCar => "widget_chip_cable",
			TransitMode.Walk => "widget_chip_walk",
			_ => "widget_chip_default"
		};

	// ---------- Taps ----------

	private static PendingIntent RefreshIntent(Context context, int widgetId, WidgetKind kind)
	{
		var intent = new Intent(context, WidgetNames.ProviderOf(kind));

		intent.SetAction(WidgetNames.Refresh);
		intent.PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId);

		return PendingIntent.GetBroadcast(context, widgetId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
	}

	private static PendingIntent OpenAppIntent(Context context, int widgetId)
	{
		var intent = new Intent(context, typeof(MainActivity));

		intent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);

		return PendingIntent.GetActivity(context, OpenCode + widgetId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
	}

	private static PendingIntent ConfigureIntent(Context context, int widgetId)
	{
		var intent = new Intent(AppWidgetManager.ActionAppwidgetConfigure);

		intent.SetClassName(context, WidgetNames.Config);
		intent.PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId);

		return PendingIntent.GetActivity(context, ConfigureCode + widgetId, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
	}
}
