using DDjourneys.Core.Contract;
using Android.App;
using Android.Content;
using DDjourneys.Localization;

namespace DDjourneys.Platforms.Android.Disruptions;

/// <summary>
/// The notification of the watched lines' background check: one slot on a channel of its own, replaced by
/// the next change. A tap opens the disruptions page the way the replan button does: an explicit VIEW
/// intent of the activity (the trampoline rule) that carries the contract's disruptions link; the inbox
/// opens the page once the shell is ready, on a cold start included.
/// </summary>
internal static class WatchedLineNotification
{
	private const string ChannelId = "watched-lines";

	/// <summary>One slot: a new change replaces the one before it, nothing stacks up.</summary>
	private const int NotificationId = 7316;

	private const string StatusIconName = "ic_stat_journey";

	private static Context Context => global::Android.App.Application.Context;

	private static NotificationManager? Manager =>
		Context.GetSystemService(Context.NotificationService) as NotificationManager;

	/// <summary>Creates the channel (idempotent, beside the tracking channels of the live notification).</summary>
	public static void EnsureChannel()
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26) || Manager is not { } manager)
		{
			return;
		}

		manager.CreateNotificationChannel(
			new NotificationChannel(
				ChannelId,
				LocalizationService.Current.CurrentStrings.WatchedLines.ChannelWatched,
				NotificationImportance.Default));
	}

	/// <summary>Shows the change: the one line that moved on, or the list when several did.</summary>
	public static void Show(string text)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(text);

		try
		{
			Context context = Context;

			EnsureChannel();

			Notification.Builder builder =
				OperatingSystem.IsAndroidVersionAtLeast(26)
					? new Notification.Builder(context, ChannelId)
					: new Notification.Builder(context);

			builder
				.SetSmallIcon(ResourceId(StatusIconName, global::Android.Resource.Drawable.IcMenuDirections))!
				.SetContentTitle(LocalizationService.Current.CurrentStrings.WatchedLines.NotifTitle)!
				.SetContentText(text)!
				.SetStyle(new Notification.BigTextStyle().BigText(text))!
				.SetAutoCancel(true)!
				.SetShowWhen(true)!
				.SetWhen(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!
				.SetVisibility(NotificationVisibility.Public)!
				.SetCategory(Notification.CategoryEvent);

			if (OpenDisruptionsIntent(context) is { } open)
			{
				builder.SetContentIntent(open);
			}

			Manager?.Notify(NotificationId, builder.Build()!);
		}
		catch (Java.Lang.SecurityException)
		{
			// Notification permission denied (Android 13+; only the tracking flow asks for it): the check itself stays.
		}
	}

	/// <summary>Drawable by name, so the code builds without the generated resource class.</summary>
	private static int ResourceId(string name, int fallback)
	{
		Context context = Context;

		int id =
			context.Resources?.GetIdentifier(name, "drawable", context.PackageName) ?? 0;

		return id != 0 ? id : fallback;
	}

	// The replan precedent: the intent starts the activity itself (never a receiver), and instead of state of
	// its own it carries the contract's disruptions link, which MainActivity hands to the inbox.
	private static PendingIntent OpenDisruptionsIntent(Context context)
	{
		Intent target =
			new Intent(context, typeof(global::DDjourneys.Platforms.Android.MainActivity))
				.SetAction(Intent.ActionView)!
				.SetData(global::Android.Net.Uri.Parse(ContractLinks.Disruptions().ToString()))!
				.SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop)!;

		return PendingIntent.GetActivity(
			context,
			0,
			target,
			PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
	}
}
