using System.Globalization;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Service.Notification;
using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Localization;
using DDjourneys.Platforms.Android.LiveJourney.Schutzengel;
using DDjourneys.Tracking.Schutzengel;

namespace DDjourneys.Platforms.Android.LiveJourney;

/// <summary>
/// Builds and posts the notifications. The live notification uses the Android 16 progress style
/// and asks to be promoted to a live update; older versions get the same content as plain text.
/// </summary>
internal static class LiveJourneyNotification
{
	private const string LiveChannelId = "live-journey";
	private const string AlertChannelId = "journey-alerts";

	/// <summary>Id of the foreground service notification, which is also the live notification.</summary>
	public const int LiveNotificationId = 7314;

	// The platform-neutral action names; their values are baked into pending intents.
	internal const string ActionPause = TrackingActions.Pause;
	internal const string ActionStop = TrackingActions.Stop;
	internal const string ActionDismissed = TrackingActions.Dismissed;
	internal const string ActionOpen = TrackingActions.Open;
	internal const string ExtraPlanId = TrackingActions.PlanIdKey;

	/// <summary>
	/// Group of the alerts only. The live notification is deliberately in no group: a group child can
	/// be folded into a collapsed stack behind its summary, and the live notification must always stand
	/// on its own (alerts and their stack can never hide, collapse or bundle it).
	/// </summary>
	private const string GroupKey = "dd.journey.alerts";

	/// <summary>Id of the alert stack summary; only posted while two or more alerts are visible.</summary>
	private const int SummaryNotificationId = 7315;

	/// <summary>More alerts than this are pointless for a traveller; the oldest are removed.</summary>
	private const int MaxAlerts = 8;

	private const string StatusIconName = "ic_stat_journey";
	private const string StopIconName = "ic_live_stop";

	// The app's accent (colors.xml), so the notification reads as part of the app.
	private static readonly global::Android.Graphics.Color AccentColor =
		global::Android.Graphics.Color.Rgb(11, 110, 138);

	private static readonly global::Android.Graphics.Color RideColor = AccentColor;

	private static readonly global::Android.Graphics.Color ChangeColor =
		global::Android.Graphics.Color.Rgb(217, 119, 6);

	private static readonly global::Android.Graphics.Color ArrivedColor =
		global::Android.Graphics.Color.Rgb(22, 163, 74);

	private static readonly global::Android.Graphics.Color RiskColor =
		global::Android.Graphics.Color.Rgb(220, 38, 38);

	private static readonly global::Android.Graphics.Color WalkColor =
		global::Android.Graphics.Color.Rgb(150, 156, 168);

	private static readonly global::Android.Graphics.Color PointColor =
		global::Android.Graphics.Color.Rgb(70, 76, 88);

	private static readonly Lock Gate = new();

	private static Notification? _last;

	/// <summary>The notification most recently built for the live slot; used when the service starts.</summary>
	public static Notification? Last
	{
		get
		{
			lock (Gate)
			{
				return _last;
			}
		}
	}

	private static Context Context => global::Android.App.Application.Context;

	private static NotificationManager? Manager =>
		Context.GetSystemService(Context.NotificationService) as NotificationManager;

	private static TrackingStrings Strings =>
		LocalizationService.Current.CurrentStrings.Tracking;

	// ----- Live notification -----

	public static void Show(LiveJourneyContent content)
	{
		try
		{
			Notification notification = Build(content);

			Remember(notification);

			Manager?.Notify(LiveNotificationId, notification);

			RefreshSummary();
		}
		catch (Java.Lang.SecurityException)
		{
			// Notification permission denied: tracking continues silently.
		}
	}

	public static void Remember(Notification notification)
	{
		lock (Gate)
		{
			_last = notification;
		}
	}

	public static Notification BuildFallback() =>
		Build(SchutzengelLiveContent.Monitoring(Strings));

	public static void Dismiss()
	{
		lock (Gate)
		{
			_last = null;
		}

		Manager?.Cancel(LiveNotificationId);

		RefreshSummary();
	}

	public static Notification Build(LiveJourneyContent content)
	{
		ArgumentNullException.ThrowIfNull(content);

		Context context = Context;

		EnsureChannels();

		Notification.Builder builder =
			OperatingSystem.IsAndroidVersionAtLeast(26)
				? new Notification.Builder(context, LiveChannelId)
				: new Notification.Builder(context);

		builder
			.SetSmallIcon(ResourceId(StatusIconName, global::Android.Resource.Drawable.IcMenuDirections))!
			.SetColor(PhaseColor(content.Phase))!
			.SetContentTitle(content.Title)!
			.SetContentText(content.Text)!
			.SetOngoing(content.Ongoing)!
			.SetOnlyAlertOnce(true)!
			.SetAutoCancel(!content.Ongoing)!
			.SetCategory(Notification.CategoryProgress)!
			.SetVisibility(NotificationVisibility.Public)!
			.SetSortKey("0")!
			.SetShowWhen(false);

		if (content.SubText is { Length: > 0 } sub)
		{
			builder.SetSubText(sub);
		}

		// A tap on the body opens the followed journey on the overview page.
		if (OpenAppIntent(context, content.PlanId) is { } open)
		{
			builder.SetContentIntent(open);
		}

		if (content.PlanId.Length > 0)
		{
			builder.SetDeleteIntent(
				ActionIntent(context, ActionDismissed, content.PlanId));

			if (content.Ongoing)
			{
				builder.AddAction(
					CreateAction(context, Strings.NotifActionPause, ActionPause, content.PlanId, global::Android.Resource.Drawable.IcMediaPause));

				builder.AddAction(
					CreateAction(context, Strings.NotifActionStop, ActionStop, content.PlanId, global::Android.Resource.Drawable.IcMenuCloseClearCancel));
			}
		}

		if (OperatingSystem.IsAndroidVersionAtLeast(36))
		{
			ApplyLiveUpdate(builder, context, content);
		}
		else
		{
			ApplyClassicProgress(builder, content);
		}

		return builder.Build()!;
	}

	/// <summary>Before Android 16: a real progress bar and a system-driven countdown to the next event.</summary>
	private static void ApplyClassicProgress(Notification.Builder builder, LiveJourneyContent content)
	{
		builder.SetStyle(new Notification.BigTextStyle().BigText(content.Text));

		int total = content.Segments.Sum();

		if (total > 0)
		{
			builder.SetProgress(total, Math.Clamp(content.Position, 0, total), false);
		}

		if (content.When is { } when
			&& when > DateTimeOffset.UtcNow)
		{
			builder
				.SetWhen(when.ToUnixTimeMilliseconds())!
				.SetShowWhen(true)!
				.SetUsesChronometer(true)!
				.SetChronometerCountDown(true);
		}
	}

	/// <summary>Puts an alert into the alert group; the summary itself stays silent.</summary>
	private static void ApplyGroup(Notification.Builder builder, bool summary = false)
	{
		builder.SetGroup(GroupKey);

		if (summary)
		{
			builder.SetGroupSummary(true);
		}

		if (OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			builder.SetGroupAlertBehavior(NotificationGroupAlertBehavior.Children);
		}
	}

	private static global::Android.Graphics.Color PhaseColor(TrackingPhase phase) =>
		phase switch
		{
			TrackingPhase.AtRisk or TrackingPhase.Cancelled => RiskColor,
			TrackingPhase.AtInterchange => ChangeColor,
			TrackingPhase.Arrived => ArrivedColor,
			_ => AccentColor
		};

	/// <summary>Drawable by name, so the code builds without the generated resource class.</summary>
	private static int ResourceId(string name, int fallback)
	{
		Context context = Context;

		int id =
			context.Resources?.GetIdentifier(name, "drawable", context.PackageName) ?? 0;

		return id != 0 ? id : fallback;
	}

	[System.Runtime.Versioning.SupportedOSPlatform("android36.0")]
	private static void ApplyLiveUpdate(Notification.Builder builder, Context context, LiveJourneyContent content)
	{
		// Promotion to a live update needs the 36.1 platform; 36.0 shows the same notification as a normal ongoing one.
		if (content.Ongoing && OperatingSystem.IsAndroidVersionAtLeast(36, 1))
		{
			RequestPromotion(builder);
		}

		if (content.ShortText is { Length: > 0 } shortText)
		{
			builder.SetShortCriticalText(shortText);
		}

		if (content.Segments.Count > 0)
		{
			builder.SetStyle(CreateProgressStyle(context, content));
		}
	}

	[System.Runtime.Versioning.SupportedOSPlatform("android36.1")]
	private static void RequestPromotion(Notification.Builder builder) =>
		builder.SetRequestPromotedOngoing(true);

	[System.Runtime.Versioning.SupportedOSPlatform("android36.0")]
	private static Notification.ProgressStyle CreateProgressStyle(Context context, LiveJourneyContent content)
	{
		var style = new Notification.ProgressStyle();

		int total = content.Segments.Sum();
		bool risk = content.Phase is TrackingPhase.AtRisk;

		int boundary = 0;

		for (int i = 0; i < content.Segments.Count; i++)
		{
			bool individual = i < content.Individual.Count && content.Individual[i];

			style.AddProgressSegment(
				new Notification.ProgressStyle.Segment(content.Segments[i])
					.SetColor(individual ? WalkColor : risk ? RiskColor : RideColor));

			boundary += content.Segments[i];

			if (i < content.Segments.Count - 1 && boundary > 0 && boundary < total)
			{
				style.AddProgressPoint(
					new Notification.ProgressStyle.Point(boundary).SetColor(PointColor));
			}
		}

		style.SetProgress(Math.Clamp(content.Position, 0, total));

		Icon vehicle =
			Icon.CreateWithResource(
				context,
				ResourceId(StatusIconName, global::Android.Resource.Drawable.IcMenuDirections));

		Icon stop =
			Icon.CreateWithResource(
				context,
				ResourceId(StopIconName, global::Android.Resource.Drawable.IcMenuDirections));

		style.SetProgressTrackerIcon(vehicle);
		style.SetProgressStartIcon(stop);
		style.SetProgressEndIcon(stop);

		return style;
	}

	// ----- Alerts -----

	/// <summary>A heads-up notification for one event; replaces an earlier alert of the same kind.</summary>
	public static void ShowAlert(string planId, JourneyAlertKind kind, string title, string text)
	{
		try
		{
			Context context = Context;

			EnsureChannels();

			bool silent = kind is JourneyAlertKind.Arrived;

			Notification.Builder builder =
				OperatingSystem.IsAndroidVersionAtLeast(26)
					? new Notification.Builder(context, silent ? LiveChannelId : AlertChannelId)
					: new Notification.Builder(context);

			global::Android.Graphics.Color color =
				kind switch
				{
					JourneyAlertKind.Problem => RiskColor,
					JourneyAlertKind.Change => ChangeColor,
					JourneyAlertKind.Arrived => ArrivedColor,
					_ => AccentColor
				};

			builder
				.SetSmallIcon(ResourceId(StatusIconName, global::Android.Resource.Drawable.IcMenuDirections))!
				.SetColor(color)!
				.SetContentTitle(title)!
				.SetContentText(text)!
				.SetStyle(new Notification.BigTextStyle().BigText(text))!
				.SetAutoCancel(true)!
				.SetShowWhen(true)!
				.SetWhen(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!
				.SetVisibility(NotificationVisibility.Public)!
				.SetSortKey(kind is JourneyAlertKind.Problem ? "1" : "2")!
				.SetCategory(silent ? Notification.CategoryStatus : Notification.CategoryEvent);

			ApplyGroup(builder);

			if (OpenAppIntent(context, planId) is { } open)
			{
				builder.SetContentIntent(open);
			}

			Manager?.Notify(AlertId(planId, kind), builder.Build());

			RefreshSummary();
		}
		catch (Java.Lang.SecurityException)
		{
		}
	}

	public static void ClearAlerts(string planId)
	{
		NotificationManager? manager = Manager;

		if (manager is null)
		{
			return;
		}

		foreach (JourneyAlertKind kind in Enum.GetValues<JourneyAlertKind>())
		{
			manager.Cancel(AlertId(planId, kind));
		}

		RefreshSummary();
	}

	/// <summary>
	/// Keeps the alert stack in step: with two or more alerts the system shows one expandable stack,
	/// with fewer the summary is removed again. The live notification is never part of it.
	/// </summary>
	private static void RefreshSummary()
	{
		try
		{
			if (Manager is not { } manager)
			{
				return;
			}

			List<StatusBarNotification> alerts = [];

			StatusBarNotification[]? active = manager.GetActiveNotifications();

			foreach (StatusBarNotification item in active ?? [])
			{
				if (item.Id != SummaryNotificationId
					&& item.Notification?.Group == GroupKey)
				{
					alerts.Add(item);
				}
			}

			// Alerts never pile up without bound (the system caps an app's notifications, oldest first).
			foreach (StatusBarNotification stale in alerts.OrderByDescending(item => item.PostTime).Skip(MaxAlerts).ToList())
			{
				manager.Cancel(stale.Id);
				alerts.Remove(stale);
			}

			int children = alerts.Count;

			if (children < 2)
			{
				manager.Cancel(SummaryNotificationId);

				return;
			}

			Context context = Context;

			string appName =
				context.PackageManager?.GetApplicationLabel(context.ApplicationInfo!)?.ToString() ?? "DDjourneys";

			Notification.Builder builder =
				OperatingSystem.IsAndroidVersionAtLeast(26)
					? new Notification.Builder(context, AlertChannelId)
					: new Notification.Builder(context);

			builder
				.SetSmallIcon(ResourceId(StatusIconName, global::Android.Resource.Drawable.IcMenuDirections))!
				.SetColor(AccentColor)!
				.SetContentTitle(appName)!
				.SetContentText(Strings.ChannelAlerts)!
				.SetOnlyAlertOnce(true)!
				.SetAutoCancel(true)!
				.SetShowWhen(false);

			ApplyGroup(builder, summary: true);

			if (OpenAppIntent(context) is { } open)
			{
				builder.SetContentIntent(open);
			}

			manager.Notify(SummaryNotificationId, builder.Build());
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[SCHUTZENGEL] Notification summary not updated: {ex.Message}");
		}
	}

	private static int AlertId(string planId, JourneyAlertKind kind)
	{
		// Stable across processes (string.GetHashCode is randomised per process).
		uint hash = 2166136261;

		foreach (char c in planId)
		{
			hash = (hash ^ c) * 16777619;
		}

		return 20000 + (int)((hash & 0xFFFFu) * 4u + (uint)kind);
	}

	// ----- Plumbing -----

	private static void EnsureChannels()
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26) || Manager is not { } manager)
		{
			return;
		}

		TrackingStrings strings = Strings;

		manager.CreateNotificationChannel(
			new NotificationChannel(LiveChannelId, strings.ChannelLive, NotificationImportance.Low));

		manager.CreateNotificationChannel(
			new NotificationChannel(AlertChannelId, strings.ChannelAlerts, NotificationImportance.High));
	}

	/// <summary>
	/// Opens the app; with a plan id, on that followed journey. The intent is explicit (MainActivity)
	/// and carries <see cref="ActionOpen"/> and the plan id; MainActivity hands it to the app's
	/// navigation. SingleTop + ClearTop make a running instance receive it in OnNewIntent instead of
	/// merely being brought to the front.
	/// </summary>
	private static PendingIntent? OpenAppIntent(Context context, string? planId)
	{
		if (!string.IsNullOrEmpty(planId))
		{
			Intent target =
				new Intent(context, typeof(global::DDjourneys.MainActivity))
					.SetAction(ActionOpen)!
					.PutExtra(ExtraPlanId, planId)!
					.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop)!;

			return PendingIntent.GetActivity(
				context,
				RequestCode(ActionOpen, planId),
				target,
				PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
		}

		return OpenAppIntent(context);
	}

	private static PendingIntent? OpenAppIntent(Context context)
	{
		Intent? launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);

		if (launch is null)
		{
			return null;
		}

		launch.SetFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);

		return PendingIntent.GetActivity(
			context,
			0,
			launch,
			PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
	}

	private static PendingIntent? ActionIntent(Context context, string action, string planId)
	{
		Intent intent =
			new Intent(context, typeof(JourneyNotificationActionReceiver))
				.SetAction(action)!
				.SetPackage(context.PackageName)!
				.PutExtra(ExtraPlanId, planId)!;

		return PendingIntent.GetBroadcast(
			context,
			RequestCode(action, planId),
			intent,
			PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
	}

	private static Notification.Action CreateAction(
		Context context,
		string label,
		string action,
		string planId,
		int iconResource) =>
		new Notification.Action.Builder(
			Icon.CreateWithResource(context, iconResource),
			label,
			ActionIntent(context, action, planId))
			.Build()!;

	private static int RequestCode(string action, string planId)
	{
		uint hash = 2166136261;

		foreach (char c in action + "|" + planId)
		{
			hash = (hash ^ c) * 16777619;
		}

		return (int)(hash & 0x7FFFFFFF);
	}
}