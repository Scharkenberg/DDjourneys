using DDjourneys.Contract;
using Microsoft.Windows.AppLifecycle;
using ProtocolActivatedEventArgs = global::Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;

namespace DDjourneys.Platforms.Windows;

/// <summary>
/// Protocol activation (<c>ddjourneys://</c>, registered in Package.appxmanifest). The app is single-instance:
/// a second launch hands its activation to the running instance and exits, so a link from another app lands
/// in the window the user already has. (.NET 11: the OnAppInstanceActivated lifecycle hook receives the
/// initial and every redirected activation.)
/// </summary>
internal static class WindowsContractActivation
{
	private const string InstanceKey = "dev.Scharkenberg.DDjourneys.main";

	/// <returns>True when the activation was fully handled here (a redirected instance must not open a window).</returns>
	public static bool Handle(Microsoft.UI.Xaml.Application application, AppActivationArguments args)
	{
		try
		{
			AppInstance keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);

			if (!keyInstance.IsCurrent)
			{
				_ = RedirectAndExitAsync(application, keyInstance, args);

				return true;
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
			System.Diagnostics.Debug.WriteLine($"Activation handling failed: {ex.Message}");
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
			System.Diagnostics.Debug.WriteLine($"Redirecting the activation failed: {ex.Message}");
		}
		finally
		{
			application.Exit();
		}
	}
}
