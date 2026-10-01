using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal static class LiveJourneyNotification
{
	private const string ChannelId = "live-journey";
	private const int NotificationId = 7314;

	public static void Show(LiveJourneyState state)
	{
		var context = global::Android.App.Application.Context;
		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
		if (manager is null) return;

		try { manager.Notify(NotificationId, Build(state)); }
		catch (Java.Lang.SecurityException) { /* Android 13+ may require notification permission. */ }
	}

	public static Notification Build(LiveJourneyState state)
	{
		var context = global::Android.App.Application.Context;
		if (OperatingSystem.IsAndroidVersionAtLeast(26)
			&& context.GetSystemService(Context.NotificationService) is NotificationManager manager)
			manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Live journey", NotificationImportance.Low));
		var title = state.Phase switch
		{
			TrackingPhase.AtRisk => "Connection at risk",
			TrackingPhase.Cancelled => "Journey cancelled",
			TrackingPhase.Arrived => "Arrived",
			_ => "Journey in progress"
		};
		var text = state.Message ?? $"Leg {Math.Min(state.CurrentLegIndex + 1, state.LegCount)} of {state.LegCount}";
		var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
			? new Notification.Builder(context, ChannelId)
			: new Notification.Builder(context);
		builder
			.SetSmallIcon(global::Android.Resource.Drawable.IcMenuDirections)
			.SetContentTitle(title)
			.SetContentText(text)
			.SetOngoing(state.Phase is not (TrackingPhase.Cancelled or TrackingPhase.Arrived))
			.SetOnlyAlertOnce(true);
		builder.AddAction(CreateAction(context, "Pause", "deactivate", 7315));
		builder.AddAction(CreateAction(context, "Delete", "delete", 7316));

		if (OperatingSystem.IsAndroidVersionAtLeast(36) && state.LegCount > 0)
		{
			var progress = new Notification.ProgressStyle().SetStyledByProgress(true);
			var length = Math.Max(1, state.LegCount) * 1000;
			progress.AddProgressPoint(new Notification.ProgressStyle.Point(1).SetColor(global::Android.Graphics.Color.Rgb(70, 76, 88)));
			for (var i = 0; i < state.LegCount; i++)
				progress.AddProgressSegment(new Notification.ProgressStyle.Segment(1000).SetColor(global::Android.Graphics.Color.Rgb(234, 182, 44)));
			for (var i = 1; i < state.LegCount; i++)
				progress.AddProgressPoint(new Notification.ProgressStyle.Point(i * 1000).SetColor(global::Android.Graphics.Color.Rgb(70, 76, 88)));
			if (length > 1)
				progress.AddProgressPoint(new Notification.ProgressStyle.Point(length - 1).SetColor(global::Android.Graphics.Color.Rgb(70, 76, 88)));
			progress.SetProgress(Math.Clamp(state.CurrentLegIndex * 1000 + 500, 0, length));
			progress.SetProgressTrackerIcon(Icon.CreateWithResource(context, global::Android.Resource.Drawable.IcMenuDirections));
			progress.SetProgressStartIcon(Icon.CreateWithResource(context, global::Android.Resource.Drawable.IcMenuDirections));
			progress.SetProgressEndIcon(Icon.CreateWithResource(context, global::Android.Resource.Drawable.IcMenuDirections));
			builder.SetStyle(progress);
		}
		else
		{
			builder.SetStyle(new Notification.BigTextStyle().BigText($"{text}. Planned arrival: {state.PlannedArrival?.ToLocalTime():t}"));
		}

		return builder.Build();
	}

	private static Notification.Action CreateAction(Context context, string label, string action, int requestCode)
	{
		var intent = new Intent(context, typeof(JourneyNotificationActionReceiver)).SetAction(action);
		var pending = PendingIntent.GetBroadcast(context, requestCode, intent,
			PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
		return new Notification.Action.Builder(
			Icon.CreateWithResource(context, global::Android.Resource.Drawable.IcMediaPause), label, pending).Build();
	}

	public static void Dismiss()
	{
		var context = global::Android.App.Application.Context;
		((NotificationManager?)context.GetSystemService(Context.NotificationService))?.Cancel(NotificationId);
	}
}
