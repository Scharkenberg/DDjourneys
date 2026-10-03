using DDjourneys.Core.Contract;

namespace DDjourneys.Contract;

/// <summary>Opens a callback URI (the other app). Platform-specific.</summary>
public interface ICallbackLauncher
{
	/// <summary>True when something took the link; false when no app can open it.</summary>
	Task<bool> OpenAsync(Uri uri);
}

/// <summary>Sends replies to callers: builds the callback URI, re-checks it, opens it. Never throws.</summary>
public sealed class ContractResponder(ICallbackLauncher launcher)
{
	/// <summary>False when there was nothing to send to, or the other app could not be opened.</summary>
	public async Task<bool> SendAsync(
		ContractReply reply,
		ContractCallbacks callbacks,
		params string[] droppable)
	{
		ArgumentNullException.ThrowIfNull(reply);
		ArgumentNullException.ThrowIfNull(callbacks);

		try
		{
			Uri? uri = reply.ToCallbackUri(callbacks, droppable);

			// The policy ran at parse time; running it again costs nothing and guards the boundary.
			if (uri is null || !ContractCallbackPolicy.IsAllowed(uri))
			{
				return false;
			}

			return await MainThread.InvokeOnMainThreadAsync(() => launcher.OpenAsync(uri));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Contract reply failed: {ex.Message}");

			return false;
		}
	}
}

/// <summary>The platform's way to open another app's link.</summary>
internal sealed class PlatformCallbackLauncher : ICallbackLauncher
{
	public Task<bool> OpenAsync(Uri uri)
	{
		try
		{
#if ANDROID
			// No visibility query needed: starting an activity for a VIEW intent works for any scheme.
			var intent = new global::Android.Content.Intent(
				global::Android.Content.Intent.ActionView,
				global::Android.Net.Uri.Parse(uri.AbsoluteUri));

			intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);

			global::Android.App.Application.Context.StartActivity(intent);

			return Task.FromResult(true);
#else
			return Launcher.Default.TryOpenAsync(uri);
#endif
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Opening the callback failed: {ex.Message}");

			return Task.FromResult(false);
		}
	}
}
