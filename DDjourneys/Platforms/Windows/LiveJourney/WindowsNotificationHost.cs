using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Windows.AppNotifications;

namespace DDjourneys.Platforms.Windows.LiveJourney;

/// <summary>
/// Registers the app for app notifications and routes taps and buttons to the tracker. Cold start:
/// Windows launches the app, the activation arrives through <see cref="Handle"/>.
/// </summary>
internal static class WindowsNotificationHost
{
	internal const string LiveGroup = "dd.journey.live";
	internal const string AlertGroup = "dd.journey.alert";
	internal const string ActionKey = "action";

	private static readonly TimeSpan Duplicate = TimeSpan.FromMilliseconds(3000);

	private static readonly Lock Gate = new();

	private static IServiceProvider? _services;
	private static int _initialized;
	private static int _registered;
	private static MauiApp? _headless;
	private static string _lastKey = string.Empty;
	private static DateTimeOffset _lastAt;

	/// <summary>
	/// True when Windows started this process only to deliver a notification activation (COM server,
	/// "-Embedding"): MAUI creates no app and no window then.
	/// </summary>
	internal static bool Embedded { get; private set; }

	private static readonly string PendingPath =
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"DDjourneys",
			"pending-open.txt");

	/// <summary>Called first thing in the process: a notification activation must find a registered app.</summary>
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

	/// <summary>Called when the services exist (a normal start with UI).</summary>
	public static void Initialize(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);

		if (Interlocked.Exchange(ref _initialized, 1) == 1)
		{
			return;
		}

		_services = services;

		_ = RemoveLeftoversAsync(AppNotificationManager.Default);

		try
		{
			// A click that found no UI process left its wish here.
			if (File.Exists(PendingPath))
			{
				string planId = File.ReadAllText(PendingPath).Trim();

				File.Delete(PendingPath);

				WindowsTrace.Write($"Pending open for plan '{planId}'");

				_ = Task.Run(() => DispatchAsync(TrackingActions.Open, planId));
			}
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Reading the pending open failed", ex);
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
				action is TrackingActions.Pause or TrackingActions.Stop or TrackingActions.Dismissed or TrackingActions.Open;

			if (!known || (action != TrackingActions.Open && planId.Length == 0))
			{
				WindowsTrace.Write("Activation ignored (unknown or incomplete)");

				return;
			}

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
			if (Embedded && _services is null)
			{
				await DispatchEmbeddedAsync(action, planId);

				return;
			}

			// A start with UI can deliver the activation before the services exist.
			for (int i = 0; i < 100 && _services is null; i++)
			{
				await Task.Delay(100);
			}

			if (_services is not { } services)
			{
				WindowsTrace.Write("No services; activation dropped");

				return;
			}

			await RunActionAsync(services, action, planId);
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Dispatching the activation failed", ex);
		}
	}

	/// <summary>
	/// Windows started this process just for the click, so there is no UI and no MAUI app. A tap
	/// starts the real app (which picks up the wish); the buttons run against a headless tracker.
	/// </summary>
	private static async Task DispatchEmbeddedAsync(string action, string planId)
	{
		try
		{
			if (action == TrackingActions.Open)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(PendingPath)!);
				File.WriteAllText(PendingPath, planId);

				IReadOnlyList<global::Windows.ApplicationModel.Core.AppListEntry> entries =
					await global::Windows.ApplicationModel.Package.Current.GetAppListEntriesAsync();

				bool launched = entries.Count > 0 && await entries[0].LaunchAsync();

				WindowsTrace.Write($"Started the app for a tap: {launched}");

				return;
			}

			_headless ??= MauiProgram.CreateMauiApp();

			_services = _headless.Services;

			WindowsTrace.Write("Headless services created");

			if (_services.GetService<TrackingCallbackBridge>() is { } bridge)
			{
				// Resolving the tracker attaches it; resuming loads the followed journeys.
				_ = _services.GetService<IJourneyTracker>();

				await bridge.ResumeAsync();
			}

			await RunActionAsync(_services, action, planId);

			// Let pending effects (removing the notification) finish.
			await Task.Delay(TimeSpan.FromSeconds(3));
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Headless handling failed", ex);
		}
		finally
		{
			WindowsTrace.Write("Embedded process exits");

			Environment.Exit(0);
		}
	}

	private static async Task RunActionAsync(IServiceProvider services, string action, string planId)
	{
		// Resolving the tracker creates it and attaches it to the bridge.
		_ = services.GetService<IJourneyTracker>();

		if (action == TrackingActions.Open)
		{
			services.GetService<TrackedJourneyNavigator>()?.Request(planId);

			await ActivateWindowAsync();

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
				// Cold start: the followed journeys are not loaded yet, an action would find nothing.
				await bridge.ResumeAsync();

				WindowsTrace.Write("Followed journeys loaded");
			}

			await bridge.HandleActionAsync(action, planId);
		}

		WindowsTrace.Write($"{action} handled");
	}

	private static async Task ActivateWindowAsync()
	{
		for (int i = 0; i < 80; i++)
		{
			try
			{
				bool done =
					await MainThread.InvokeOnMainThreadAsync(
						() =>
						{
							if (Microsoft.Maui.Controls.Application.Current is { } app
								&& app.Windows.FirstOrDefault() is { } window)
							{
								app.ActivateWindow(window);

								return true;
							}

							return false;
						});

				if (done)
				{
					return;
				}
			}
			catch (InvalidOperationException)
			{
				// The main thread is not there yet (cold start).
			}

			await Task.Delay(250);
		}

		WindowsTrace.Write("No window to activate");
	}
}
