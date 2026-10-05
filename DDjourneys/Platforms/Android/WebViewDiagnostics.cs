using System.Globalization;
using DDjourneys.Core.Diagnostics;
using AActivityManager = global::Android.App.ActivityManager;
using AConsoleMessage = global::Android.Webkit.ConsoleMessage;
using AContext = global::Android.Content.Context;
using ALog = global::Android.Util.Log;
using ABuild = global::Android.OS.Build;
using AWebChromeClient = global::Android.Webkit.WebChromeClient;
using AWebView = global::Android.Webkit.WebView;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Everything that helps to find out why a web view (the map, a notice) shows nothing on a device, written to the
/// log file and mirrored to the system log under the tag <c>DDjourneys</c> (<c>adb logcat -s DDjourneys</c>), only while
/// "Log to file" is on:
/// <list type="bullet">
/// <item>the device, the Android version, the memory, the OpenGL ES version, the web view package and its version;</item>
/// <item>every console message of the page with file and line (the page's own errors arrive as <c>log:</c> messages);</item>
/// <item>loading progress, and, when the page has not reported that it is ready after 5, 12 and 30 seconds, a probe run
/// natively inside it: address, state, what is defined, and everything the page logged so far;</item>
/// <item>managed exceptions nobody handled.</item>
/// </list>
/// Lines are written one by one and at once, so after a crash of the whole process (a web view on Android 7 renders
/// inside the app's own process) the last line in the file is the last thing that happened.
/// </summary>
internal static class WebViewDiagnostics
{
	private const string Tag = "DDjourneys";
	private const int ChunkLength = 3500;

	private static readonly int[] ProbeSeconds = [5, 12, 30];

	/// <summary>Mirrors the log to the system log and records unhandled managed exceptions. Once, at start.</summary>
	public static void Install()
	{
		DiagnosticLog.Sink = Mirror;

		global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser +=
			(_, e) => DiagnosticLog.Write($"[Crash] unhandled managed exception (handled by the app: {e.Handled}): {e.Exception}");
	}

	private static void Mirror(string line)
	{
		for (int start = 0; start < line.Length; start += ChunkLength)
		{
			ALog.Info(Tag, line.Substring(start, Math.Min(ChunkLength, line.Length - start)));
		}
	}

	/// <summary>Called when a web view exists. Does nothing while logging is off.</summary>
	public static void Attach(AWebView web, string name, Func<bool> isReady)
	{
		ArgumentNullException.ThrowIfNull(web);
		ArgumentNullException.ThrowIfNull(isReady);

		if (!DiagnosticLog.Enabled)
		{
			return;
		}

		try
		{
			LogEnvironment(name);
			LogSettings(web, name);

			web.SetWebChromeClient(new LoggingChromeClient(name));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[WebView {name}] diagnostics could not be attached: {ex}");
		}

		_ = WatchAsync(web, name, isReady);
	}

	private static void LogEnvironment(string name)
	{
		DiagnosticLog.Write(
			$"[WebView {name}] device: {ABuild.Manufacturer} {ABuild.Model} ({ABuild.Product}, hardware {ABuild.Hardware}), "
			+ $"Android {ABuild.VERSION.Release} (API {(int)ABuild.VERSION.SdkInt}), abi {string.Join('/', ABuild.SupportedAbis ?? [])}");

		try
		{
			var memory = new AActivityManager.MemoryInfo();

			if (global::Android.App.Application.Context.GetSystemService(AContext.ActivityService) is AActivityManager manager)
			{
				manager.GetMemoryInfo(memory);

				DiagnosticLog.Write(
					string.Create(
						CultureInfo.InvariantCulture,
						$"[WebView {name}] memory: {memory.AvailMem / 1048576} MB available of {memory.TotalMem / 1048576} MB, low={memory.LowMemory}, "
						+ $"app heap limit {Java.Lang.Runtime.GetRuntime()?.MaxMemory() / 1048576} MB, large heap class {manager.LargeMemoryClass} MB, "
						+ $"OpenGL ES {manager.DeviceConfigurationInfo?.GlEsVersion}"));
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[WebView {name}] memory not read: {ex.Message}");
		}

		LogWebViewPackages(name);
	}

	/// <summary>The engine behind the web view: the package that provides it and its version (Chromium 51 cannot run MapLibre GL JS 5).</summary>
	private static void LogWebViewPackages(string name)
	{
		try
		{
			if (OperatingSystem.IsAndroidVersionAtLeast(28)
				&& AWebView.CurrentWebViewPackage is { } current)
			{
				DiagnosticLog.Write($"[WebView {name}] engine: {current.PackageName} {current.VersionName} (code {current.LongVersionCode})");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[WebView {name}] engine package not read: {ex.Message}");
		}

		// Before Android 8 the package is not asked for: every candidate is looked up.
		foreach (string package in new[] { "com.google.android.webview", "com.android.webview", "com.android.chrome", "com.google.android.trichrome.library" })
		{
			try
			{
				if (global::Android.App.Application.Context.PackageManager?.GetPackageInfo(package, (global::Android.Content.PM.PackageInfoFlags)0) is { } info)
				{
					DiagnosticLog.Write($"[WebView {name}] installed: {package} {info.VersionName}, updated {DateTimeOffset.FromUnixTimeMilliseconds(info.LastUpdateTime):yyyy-MM-dd}");
				}
			}
			catch (Exception)
			{
				// Not installed: nothing to say.
			}
		}
	}

	private static void LogSettings(AWebView web, string name)
	{
		global::Android.Webkit.WebSettings? settings = web.Settings;

		DiagnosticLog.Write(
			$"[WebView {name}] view: hardware accelerated={web.IsHardwareAccelerated}, layer={web.LayerType}, size={web.Width}x{web.Height}, "
			+ $"javascript={settings?.JavaScriptEnabled}, domStorage={settings?.DomStorageEnabled}, fileAccess={settings?.AllowFileAccess}, "
			+ $"mixedContent={settings?.MixedContentMode}, textZoom={settings?.TextZoom}");

		DiagnosticLog.Write($"[WebView {name}] user agent: {settings?.UserAgentString}");
	}

	/// <summary>While the page has not said it is ready, looks inside it now and then.</summary>
	private static async Task WatchAsync(AWebView web, string name, Func<bool> isReady)
	{
		int waited = 0;

		foreach (int seconds in ProbeSeconds)
		{
			await Task.Delay(TimeSpan.FromSeconds(seconds - waited)).ConfigureAwait(false);
			waited = seconds;

			if (isReady())
			{
				DiagnosticLog.Write($"[WebView {name}] page reported ready within {seconds} s");

				return;
			}

			await ProbeAsync(web, name, seconds).ConfigureAwait(false);
		}
	}

	private static async Task ProbeAsync(AWebView web, string name, int seconds)
	{
		const string script =
			"(function(){try{return JSON.stringify({href:location.href,readyState:document.readyState,hybrid:typeof window.HybridWebView,"
			+ "maplibre:typeof window.maplibregl,diagnostics:typeof window.ddDump,body:document.body?document.body.children.length:-1,"
			+ "log:window.ddDump?window.ddDump():null});}catch(e){return 'probe failed: '+e;}})()";

		var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			string state = string.Empty;

			await MainThread.InvokeOnMainThreadAsync(
				() =>
				{
					state =
						$"url={web.Url}, progress={web.Progress}%, contentHeight={web.ContentHeight}, size={web.Width}x{web.Height}, shown={web.IsShown}, visibility={web.Visibility}, attached={web.IsAttachedToWindow}";

					web.EvaluateJavascript(script, new ProbeCallback(answer));
				}).ConfigureAwait(false);

			DiagnosticLog.Write($"[WebView {name}] NOT READY after {seconds} s: {state}");

			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
			Task timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token);

			Task finished = await Task.WhenAny(answer.Task, timeoutTask).ConfigureAwait(false);

			if (finished == answer.Task)
			{
				timeoutCts.Cancel(); // stop the timer immediately
				DiagnosticLog.Write($"[WebView {name}] probe inside the page: {await answer.Task.ConfigureAwait(false)}");
			}
			else
			{
				DiagnosticLog.Write($"[WebView {name}] the page did not answer the probe within 4 s: its JavaScript is not running (blocked, crashed or not loaded)");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[WebView {name}] probe failed: {ex}");
		}
	}

	private sealed class ProbeCallback(TaskCompletionSource<string> answer) : Java.Lang.Object, global::Android.Webkit.IValueCallback
	{
		public void OnReceiveValue(Java.Lang.Object? value) =>
			answer.TrySetResult(value?.ToString() ?? "null");
	}

	/// <summary>Console messages of the page, and how far loading is.</summary>
	private sealed class LoggingChromeClient(string name) : AWebChromeClient
	{
		private int _milestone = -1;

		public override bool OnConsoleMessage(AConsoleMessage? consoleMessage)
		{
			if (consoleMessage is not null)
			{
				DiagnosticLog.Write(
					$"[WebView {name}] console {consoleMessage.InvokeMessageLevel()}: {consoleMessage.Message()} ({consoleMessage.SourceId()}:{consoleMessage.LineNumber()})");
			}

			return base.OnConsoleMessage(consoleMessage);
		}

		public override void OnProgressChanged(AWebView? view, int newProgress)
		{
			int milestone = newProgress / 25;

			if (milestone != _milestone)
			{
				_milestone = milestone;

				DiagnosticLog.Write($"[WebView {name}] loading {newProgress}%");
			}

			base.OnProgressChanged(view, newProgress);
		}

		public override void OnReceivedTitle(AWebView? view, string? title)
		{
			DiagnosticLog.Write($"[WebView {name}] title: {title}");

			base.OnReceivedTitle(view, title);
		}
	}
}
