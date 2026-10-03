using DDjourneys.Contract;
using DDjourneys.Platforms.Windows.LiveJourney;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using ProtocolActivatedEventArgs = global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;

namespace DDjourneys.Platforms.Windows;

/// <summary>
/// Protocol activation (<c>ddjourneys://</c>, registered in Package.appxmanifest) and app notification
/// activation. The app is single-instance: a second launch hands its activation to the running instance
/// and exits, so a link from another app lands in the window the user already has. (.NET 11: the
/// OnAppInstanceActivated lifecycle hook receives the initial and every redirected activation.)
/// </summary>
internal static class WindowsContractActivation
{
	private const string InstanceKey = "dev.Scharkenberg.DDjourneys.main";

	/// <returns>True when the activation was fully handled here (a redirected instance must not open a window).</returns>
	public static bool Handle(Microsoft.UI.Xaml.Application application, AppActivationArguments args)
	{
		try
		{
			WindowsTrace.Write($"Activation kind: {args.Kind}, data: {args.Data?.GetType().Name ?? "none"}");

			AppInstance keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

			if (!keyInstance.IsCurrent)
			{
				WindowsTrace.Write("Not the current instance; redirecting");

				_ = RedirectAndExitAsync(application, keyInstance, args);

				return true;
			}

			if (args.Kind == ExtendedActivationKind.Launch)
			{
				// A cold start from a notification can arrive as a plain launch (the log showed it);
				// the notification payload is only in the process's own activation arguments.
				if (args.Data is global::Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
				{
					WindowsTrace.Write($"Launch arguments: '{launch.Arguments}'");
				}

				AppActivationArguments own = AppInstance.GetCurrent().GetActivatedEventArgs();

				WindowsTrace.Write($"Own activation kind: {own.Kind}, data: {own.Data?.GetType().Name ?? "none"}");

				if (own.Kind == ExtendedActivationKind.AppNotification
					&& own.Data is AppNotificationActivatedEventArgs fromLaunch)
				{
					WindowsNotificationHost.Handle(fromLaunch);

					return false;
				}
			}

			if (args.Kind == ExtendedActivationKind.AppNotification
				&& args.Data is AppNotificationActivatedEventArgs notification)
			{
				WindowsNotificationHost.Handle(notification);

				return false;
			}

			if (args.Kind == ExtendedActivationKind.Protocol
				&& args.Data is ProtocolActivatedEventArgs protocol)
			{
				ContractEntry.SubmitUri(protocol.Uri?.AbsoluteUri);
			}

			if (Microsoft.Maui.Controls.Application.Current is { } maui
				&& maui.Windows.FirstOrDefault() is { } window)
			{
				maui.ActivateWindow(window);
			}
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Activation handling failed", ex);
		}

		return false;
	}

	private static async Task RedirectAndExitAsync(
		Microsoft.UI.Xaml.Application application,
		AppInstance keyInstance,
		AppActivationArguments args)
	{
		try
		{
			// Await before exiting, so the running instance can still consume the activation.
			await keyInstance.RedirectActivationToAsync(args).AsTask();
		}
		catch (Exception ex)
		{
			WindowsTrace.Write("Redirecting the activation failed", ex);
		}
		finally
		{
			application.Exit();
		}
	}
}
