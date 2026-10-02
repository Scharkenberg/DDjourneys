using System.Globalization;
using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using DDjourneys.Core.Tracking;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>
/// Everything one live notification shows. Built from the trip snapshot by <see cref="Create"/>,
/// so the Android code below only translates it into platform objects.
/// </summary>
internal sealed record LiveJourneyContent(
	string PlanId,
	string Title,
	string Text,
	string? SubText,
	string? ShortText,
	IReadOnlyList<int> Segments,
	IReadOnlyList<bool> Individual,
	int Position,
	TrackingPhase Phase,
	bool Ongoing)
{
	/// <summary>Records compare lists by reference; this key compares what is displayed.</summary>
	public string Key =>
		string.Join(
			'|',
			PlanId,
			Title,
			Text,
			SubText,
			ShortText,
			Position.ToString(CultureInfo.InvariantCulture),
			Phase,
			Ongoing,
			string.Join(',', Segments));

	public static LiveJourneyContent Create(
		string planId,
		TripSnapshot snapshot,
		TrackingPhase phase,
		string? notice,
		DateTimeOffset now,
		TrackingStrings strings)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(strings);

		string origin = snapshot.Origin ?? string.Empty;
		string destination = snapshot.Destination ?? string.Empty;
		string route = string.Format(CultureInfo.CurrentCulture, strings.NotifRoute, origin, destination);

		string? delay =
			snapshot.Delay >= TimeSpan.FromMinutes(1)
				? string.Format(
					CultureInfo.CurrentCulture,
					strings.NotifDelay,
					(int)Math.Round(snapshot.Delay.TotalMinutes))
				: null;

		string title;
		string text;
		string? shortText = null;
		bool ongoing = true;

		switch (phase)
		{
			case TrackingPhase.Arrived:
				title = strings.NotifArrivedTitle;
				text = destination.Length > 0 ? destination : route;
				ongoing = false;
				break;

			case TrackingPhase.Cancelled:
				title = strings.NotifCancelledTitle;
				text = string.IsNullOrWhiteSpace(notice) ? route : notice;
				ongoing = false;
				break;

			case TrackingPhase.AtRisk:
				title = strings.NotifRiskTitle;
				text =
					snapshot.Risk is { } risk
						? string.Format(
							CultureInfo.CurrentCulture,
							risk.Missed ? strings.NotifMissedText : strings.NotifTightText,
							risk.Station)
						: string.IsNullOrWhiteSpace(notice)
							? route
							: notice;
				shortText = "!";
				break;

			case TrackingPhase.AtInterchange:
				title =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifChangeTitle,
						snapshot.CurrentStop ?? snapshot.NextStop ?? origin);
				text =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifChangeText,
						Format.TimeOrDash(snapshot.NextStopTime));
				shortText = Countdown(snapshot.NextStopTime, now, strings);
				break;

			case TrackingPhase.InProgress:
				title =
					snapshot.MotName is { Length: > 0 } line
						&& snapshot.Direction is { Length: > 0 } direction
						? string.Format(CultureInfo.CurrentCulture, strings.NotifRiding, line, direction)
						: route;
				text =
					snapshot.NextStop is { Length: > 0 } next
						? string.Format(
							CultureInfo.CurrentCulture,
							strings.NotifNext,
							next,
							Format.TimeOrDash(snapshot.NextStopTime))
						: route;
				shortText = Countdown(snapshot.NextStopTime, now, strings);
				break;

			default:
				title =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifStartsAt,
						Format.TimeOrDash(snapshot.Start));
				text = route;
				shortText = Countdown(snapshot.Start, now, strings);
				break;
		}

		int[] segments = [.. snapshot.SegmentLengths.Select(length => Math.Max(1, length))];
		int total = segments.Sum();

		return new LiveJourneyContent(
			planId,
			title,
			text,
			delay,
			shortText,
			segments,
			snapshot.SegmentIndividual,
			Math.Clamp(snapshot.Position, 0, total),
			phase,
			ongoing);
	}

	/// <summary>Content for the service notification while journeys wait for their start.</summary>
	public static LiveJourneyContent Waiting(WatchedJourney journey, TrackingStrings strings)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(strings);

		return new LiveJourneyContent(
			journey.PlanId,
			string.Format(
				CultureInfo.CurrentCulture,
				strings.NotifStartsAt,
				Format.TimeOrDash(journey.Departure)),
			string.Format(
				CultureInfo.CurrentCulture,
				strings.NotifRoute,
				journey.Origin,
				journey.Destination),
			null,
			null,
			[],
			[],
			0,
			TrackingPhase.Planned,
			true);
	}

	public static LiveJourneyContent Monitoring(TrackingStrings strings) =>
		new(
			string.Empty,
			strings.NotifMonitoring,
			string.Empty,
			null,
			null,
			[],
			[],
			0,
			TrackingPhase.Planned,
			true);

	private static string? Countdown(DateTimeOffset? target, DateTimeOffset now, TrackingStrings strings)
	{
		if (target is not { } time)
		{
			return null;
		}

		double minutes = (time - now).TotalMinutes;

		if (minutes <= 0)
		{
			return strings.NotifNow;
		}

		return minutes < 100
			? string.Format(CultureInfo.CurrentCulture, strings.NotifMinutes, (int)Math.Ceiling(minutes))
			: null;
	}
}

internal enum JourneyAlertKind
{
	Start,
	Change,
	Problem,
	Arrived
}

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

	internal const string ActionPause = "dd.journey.pause";
	internal const string ActionStop = "dd.journey.stop";
	internal const string ActionDismissed = "dd.journey.dismissed";
	internal const string ExtraPlanId = "plan_id";

	private static readonly global::Android.Graphics.Color RideColor =
		global::Android.Graphics.Color.Rgb(234, 182, 44);

	private static readonly global::Android.Graphics.Color RiskColor =
		global::Android.Graphics.Color.Rgb(221, 68, 59);

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
		Build(LiveJourneyContent.Monitoring(Strings));

	public static void Dismiss()
	{
		lock (Gate)
		{
			_last = null;
		}

		Manager?.Cancel(LiveNotificationId);
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
			.SetSmallIcon(global::Android.Resource.Drawable.IcMenuDirections)!
			.SetContentTitle(content.Title)!
			.SetContentText(content.Text)!
			.SetOngoing(content.Ongoing)!
			.SetOnlyAlertOnce(true)!
			.SetAutoCancel(!content.Ongoing)!
			.SetShowWhen(false);

		if (content.SubText is { Length: > 0 } sub)
		{
			builder.SetSubText(sub);
		}

		if (OpenAppIntent(context) is { } open)
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
		else if (content.Segments.Count > 0)
		{
			builder.SetStyle(
				new Notification.BigTextStyle()
					.BigText(
						$"{content.Text} · {Math.Round(100.0 * content.Position / Math.Max(1, content.Segments.Sum())):0} %"));
		}

		return builder.Build()!;
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

		Icon icon = Icon.CreateWithResource(context, global::Android.Resource.Drawable.IcMenuDirections);

		style.SetProgressTrackerIcon(icon);
		style.SetProgressStartIcon(icon);
		style.SetProgressEndIcon(icon);

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

			builder
				.SetSmallIcon(global::Android.Resource.Drawable.IcMenuDirections)!
				.SetContentTitle(title)!
				.SetContentText(text)!
				.SetStyle(new Notification.BigTextStyle().BigText(text))!
				.SetAutoCancel(true)!
				.SetCategory(silent ? Notification.CategoryStatus : Notification.CategoryEvent);

			if (OpenAppIntent(context) is { } open)
			{
				builder.SetContentIntent(open);
			}

			Manager?.Notify(AlertId(planId, kind), builder.Build());
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