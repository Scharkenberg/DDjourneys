using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Windows.AppNotifications;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>
/// Registers the app for app notifications and routes taps and buttons to the tracker. Windows starts
/// the app for a click when it is not running (COM server, "-Embedding"); the click then arrives through
/// <see cref="Handle"/> or <c>NotificationInvoked</c>, always with a full MAUI app behind it.
/// </summary>
internal static class WindowsNotificationHost
{
	internal const string LiveGroup = "dd.journey.live";
	internal const string AlertGroup = "dd.journey.alert";
	internal const string ActionKey = "action";

	/// <summary>Which notification a click came from; only the live one is removed and restored by the surface.</summary>
	internal const string SourceKey = "src";
	internal const string SourceLive = "live";
	internal const string SourceAlert = "alert";

	private static readonly TimeSpan Duplicate = TimeSpan.FromMilliseconds(3000);

	private static readonly Lock Gate = new();

	private static IServiceProvider? _services;
	private static int _initialized;
	private static int _registered;
	private static string _lastKey = string.Empty;
	private static DateTimeOffset _lastAt;

	/// <summary>
	/// True when Windows started this process only to deliver a notification click. MAUI still builds the
	/// app and a window then; the window stays hidden unless the click wants it.
	/// </summary>
	internal static bool Embedded { get; private set; }

	/// <summary>Called first thing in the process: a notification click must find a registered app.</summary>
	public static void Register()
	{
		if (Interlocked.Exchange(ref _registered, 1) == 1)
		{
			return;
		}

		Embedded =
			Environment.GetCommandLineArgs().Any(
				arg => arg.Contains("-Embedding", StringComparison.OrdinalIgnoreCase)
					|| arg.Contains("AppNotificationActivated", StringComparison.OrdinalIgnoreCase));

		try
		{
			AppNotificationManager manager = AppNotificationManager.Default;

			manager.NotificationInvoked += OnInvoked;
			manager.Register();

			WindowsTrace.Write($"Registered. Setting: {manager.Setting}, embedded: {Embedded}");
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Registering for app notifications failed", ex);
		}
	}

	/// <summary>Called when the services exist.</summary>
	public static void Initialize(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);

		if (Interlocked.Exchange(ref _initialized, 1) == 1)
		{
			return;
		}

		_services = services;

		// A click that started this process must not lose its notification; otherwise a leftover from a
		// dead process is cleared (the tracker shows a fresh one as soon as it knows what is going on).
		if (!Embedded)
		{
			_ = RemoveLeftoversAsync(AppNotificationManager.Default);
		}
	}

	public static void Shutdown()
	{
		try
		{
			AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
			AppNotificationManager.Default.Unregister();
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Unregistering failed", ex);
		}
	}

	private static async Task RemoveLeftoversAsync(AppNotificationManager manager)
	{
		try
		{
			await manager.RemoveByGroupAsync(LiveGroup);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Removing leftover notifications failed", ex);
		}
	}

	private static void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
	{
		WindowsTrace.Write("NotificationInvoked");

		Handle(args);
	}

	public static void Handle(AppNotificationActivatedEventArgs args)
	{
		try
		{
			string Get(string key) =>
				args.Arguments.TryGetValue(key, out string? value) ? value : string.Empty;

			string action = Get(ActionKey);
			string planId = Get(TrackingActions.PlanIdKey);

			WindowsTrace.Write($"Activation: action='{action}' plan='{planId}'");

			bool known =
				action is TrackingActions.Pause
					or TrackingActions.Resume
					or TrackingActions.Stop
					or TrackingActions.Dismissed
					or TrackingActions.Open;

			if (!known || (action != TrackingActions.Open && planId.Length == 0))
			{
				WindowsTrace.Write("Activation ignored (unknown or incomplete)");

				return;
			}

			WindowsBackground.NoteActivation();

			string key = action + "|" + planId;

			lock (Gate)
			{
				DateTimeOffset now = DateTimeOffset.UtcNow;

				if (key == _lastKey && now - _lastAt < Duplicate)
				{
					WindowsTrace.Write("Activation ignored (duplicate)");

					return;
				}

				_lastKey = key;
				_lastAt = now;
			}

			_ = Task.Run(() => DispatchAsync(action, planId));
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Handling the activation failed", ex);
		}
	}

	private static async Task DispatchAsync(string action, string planId)
	{
		try
		{
			// A start with UI can deliver the activation before the services exist.
			for (int i = 0; i < 150 && _services is null; i++)
			{
				await Task.Delay(100);
			}

			if (_services is not { } services)
			{
				WindowsTrace.Write("No services; activation dropped");

				return;
			}

			// Resolving the tracker creates it and attaches it to the bridge.
			_ = services.GetService<IJourneyTracker>();

			if (action == TrackingActions.Open)
			{
				services.GetService<TrackedJourneyNavigator>()?.Request(planId);

				await WindowsBackground.RevealAsync();

				if (services.GetService<TrackedJourneyNavigator>() is { } navigator)
				{
					await navigator.DeliverAsync();
				}

				WindowsTrace.Write("Open delivered");

				return;
			}

			if (services.GetService<TrackingCallbackBridge>() is { } bridge)
			{
				WindowsTrace.Write($"Bridge attached: {bridge.IsAttached}");

				if (Embedded)
				{
					// Cold start: the followed journeys are not loaded yet.
					await bridge.ResumeAsync();

					WindowsTrace.Write("Followed journeys loaded");
				}

				await bridge.HandleActionAsync(action, planId);
			}

			WindowsTrace.Write($"{action} handled");

			// A process that was started just for this button ends when nothing is monitored.
			if (WindowsBackground.Stealth)
			{
				WindowsBackground.ScheduleExitCheck();
			}
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Dispatching the activation failed", ex);

			if (WindowsBackground.Stealth)
			{
				WindowsBackground.ScheduleExitCheck();
			}
		}
	}
}
