using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal static class LiveJourneyNotification
{
	private const string ChannelId =
		"live-journey";

	private const int NotificationId =
		7314;


	public static void Show(
		LiveJourneyState state)
	{
		Show(
			state,
			null);
	}


	public static void Show(
		LiveJourneyState state,
		SchutzengelProgressSnapshot? progress)
	{
		var context =
			global::Android.App.Application.Context;


		var manager =
			(NotificationManager?)context.GetSystemService(
				Context.NotificationService);


		if (manager is null)
		{
			return;
		}


		try
		{
			manager.Notify(
				NotificationId,
				Build(
					state,
					progress));
		}
		catch (Java.Lang.SecurityException)
		{
		}
	}


	public static Notification Build(
		LiveJourneyState state)
	{
		return Build(
			state,
			null);
	}


	public static Notification Build(
		LiveJourneyState state,
		SchutzengelProgressSnapshot? progress)
	{
		var context =
			global::Android.App.Application.Context;


		if (OperatingSystem.IsAndroidVersionAtLeast(
			26)
			&& context.GetSystemService(
				Context.NotificationService)
				is NotificationManager manager)
		{
			manager.CreateNotificationChannel(
				new NotificationChannel(
					ChannelId,
					"Live journey",
					NotificationImportance.Low));
		}


		string title =
			state.Phase switch
			{
				TrackingPhase.AtRisk =>
					"Connection at risk",

				TrackingPhase.Cancelled =>
					"Journey cancelled",

				TrackingPhase.Arrived =>
					"Arrived",

				TrackingPhase.AtInterchange =>
					"Changing",

				TrackingPhase.Paused =>
					"Journey paused",

				TrackingPhase.Planned =>
					"Journey planned",

				_ =>
					"Journey in progress"
			};


		string text =
			BuildText(
				state,
				progress);


		var builder =
			OperatingSystem.IsAndroidVersionAtLeast(
				26)
				? new Notification.Builder(
					context,
					ChannelId)
				: new Notification.Builder(
					context);


		builder
			.SetSmallIcon(
				global::Android.Resource.Drawable.IcMenuDirections)
			.SetContentTitle(
				title)
			.SetContentText(
				text)
			.SetOngoing(
				state.Phase is not (
					TrackingPhase.Cancelled
					or TrackingPhase.Arrived))
			.SetOnlyAlertOnce(
				true);


		builder.AddAction(
			CreateAction(
				context,
				"Pause",
				"deactivate",
				7315));


		builder.AddAction(
			CreateAction(
				context,
				"Delete",
				"delete",
				7316));


		if (OperatingSystem.IsAndroidVersionAtLeast(
			36)
			&& state.LegCount > 0)
		{
			var progressStyle =
				new Notification.ProgressStyle()
					.SetStyledByProgress(
						true);


			int segmentCount =
				Math.Max(
					1,
					state.LegCount);


			int length =
				segmentCount * 1000;


			for (int i = 0;
				i < segmentCount;
				i++)
			{
				progressStyle.AddProgressSegment(
					new Notification.ProgressStyle
						.Segment(
							1000)
						.SetColor(
							global::Android.Graphics.Color.Rgb(
								234,
								182,
								44)));
			}


			for (int i = 1;
				i < segmentCount;
				i++)
			{
				progressStyle.AddProgressPoint(
					new Notification.ProgressStyle
						.Point(
							i * 1000)
						.SetColor(
							global::Android.Graphics.Color.Rgb(
								70,
								76,
								88)));
			}


			double normalizedProgress =
				progress?.TimeProgress
				?? (
					state.Phase ==
						TrackingPhase.Arrived
						? 1
						: 0);


			normalizedProgress =
				Math.Clamp(
					normalizedProgress,
					0,
					1);


			int position =
				(int)Math.Round(
					normalizedProgress * length);


			progressStyle.SetProgress(
				Math.Clamp(
					position,
					0,
					length));


			progressStyle.SetProgressTrackerIcon(
				Icon.CreateWithResource(
					context,
					global::Android.Resource.Drawable.IcMenuDirections));


			progressStyle.SetProgressStartIcon(
				Icon.CreateWithResource(
					context,
					global::Android.Resource.Drawable.IcMenuDirections));


			progressStyle.SetProgressEndIcon(
				Icon.CreateWithResource(
					context,
					global::Android.Resource.Drawable.IcMenuDirections));


			builder.SetStyle(
				progressStyle);
		}
		else
		{
			string detail =
				progress is not null
					? $"{text}. {Math.Round(progress.TimeProgress * 100):0}% complete."
					: text;


			if (state.PlannedArrival is { } arrival)
			{
				detail +=
					$" Planned arrival: {arrival.ToLocalTime():t}.";
			}


			builder.SetStyle(
				new Notification.BigTextStyle()
					.BigText(
						detail));
		}


		return builder.Build();
	}


	private static string BuildText(
		LiveJourneyState state,
		SchutzengelProgressSnapshot? progress)
	{
		if (progress is null)
		{
			return state.Message
				?? $"Leg {Math.Min(state.CurrentLegIndex + 1, Math.Max(1, state.LegCount))} of { Math.Max(1, state.LegCount)}";
		}


		if (state.Phase ==
			TrackingPhase.Cancelled)
		{
			return "Journey cancelled";
		}


		if (state.Phase ==
			TrackingPhase.Arrived)
		{
			return progress.Message
				?? "Arrived";
		}


		string message =
			state.Message
			?? progress.Message
			?? "Monitoring journey";


		if (state.Phase ==
			TrackingPhase.AtRisk)
		{
			if (progress.NextStopName is { Length: > 0 } next)
			{
				return
					$"Connection at risk · Next: {next}";
			}


			return message;
		}


		return message;
	}


	private static Notification.Action CreateAction(
		Context context,
		string label,
		string action,
		int requestCode)
	{
		var intent =
			new Intent(
				context,
				typeof(JourneyNotificationActionReceiver))
			.SetAction(
				action);


		var pending =
			PendingIntent.GetBroadcast(
				context,
				requestCode,
				intent,
				PendingIntentFlags.UpdateCurrent
				| PendingIntentFlags.Immutable);


		return new Notification.Action.Builder(
			Icon.CreateWithResource(
				context,
				global::Android.Resource.Drawable.IcMediaPause),
			label,
			pending)
			.Build();
	}


	public static void Dismiss()
	{
		var context =
			global::Android.App.Application.Context;


		(
			(NotificationManager?)context.GetSystemService(
				Context.NotificationService))
			?.Cancel(
				NotificationId);
	}
}