using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>Whether the plan can be zoomed further in and out (for the page's zoom buttons).</summary>
public readonly record struct PlanZoom(bool CanZoomIn, bool CanZoomOut);

/// <summary>
/// Shows a plan of any size (a network map, a fare zone plan) in a <see cref="HybridWebView"/> with Leaflet
/// (Resources/Raw/wwwroot/plan.html): pinch, pan with inertia, double tap, wheel and keys, clamped to the sheet.
/// The page asks for <c>/_plan/...</c> on its own origin; those requests are answered here from the
/// <see cref="IPlanTiles"/> (<see cref="HybridWebView.WebResourceRequested"/>), so tiles come from the device,
/// whatever made them. The view owns the tiles it is given and disposes them when replaced or gone.
/// </summary>
public sealed partial class PlanView : ContentView
{
	// Addresses carry the plan version, so the web view may keep what it got for a while (back-and-forth pans).
	private static readonly IReadOnlyDictionary<string, string> Caching =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Cache-Control"] = "private, max-age=3600" };

	private readonly HybridWebView _web;
	private IPlanTiles? _tiles;
	private CancellationTokenSource _life = new();
	private bool _ready;
	private bool _subscribed;

	public PlanView()
	{
		_web =
			new HybridWebView
			{
				DefaultFile = "plan.html",
				HorizontalOptions = LayoutOptions.Fill,
				VerticalOptions = LayoutOptions.Fill
			};

		_web.RawMessageReceived += OnRawMessage;
		_web.WebResourceRequested += OnWebResourceRequested;
		_web.HandlerChanged +=
			(_, _) =>
			{
				WebBridge.PaintBackground(_web);
#if ANDROID
				if (_web.Handler?.PlatformView is Android.Webkit.WebView platformView)
				{
					DDjourneys.Platforms.Android.WebViewDiagnostics.Attach(platformView, "plan", () => _ready);
				}
#endif
			};
		WebBridge.PaintBackground(_web);

		Content = _web;

		HandlerChanged += OnHandlerChanged;
	}

	/// <summary>The first view of the plan is complete (true), or nothing of it could be drawn (false).</summary>
	public event EventHandler<bool>? Shown;

	/// <summary>The zoom reached or left one of its ends.</summary>
	public event EventHandler<PlanZoom>? ZoomChanged;

	/// <summary>Shows a plan; the view takes ownership of <paramref name="tiles"/> (the previous plan is disposed).</summary>
	public Task ShowAsync(IPlanTiles tiles)
	{
		ArgumentNullException.ThrowIfNull(tiles);

		IPlanTiles? previous = Interlocked.Exchange(ref _tiles, tiles);

		if (!ReferenceEquals(previous, tiles))
		{
			previous?.Dispose();
		}

		return _ready ? PushPlanAsync(tiles) : Task.CompletedTask;
	}

	/// <summary>Zooms by whole steps (positive: in), animated, around the centre.</summary>
	public Task ZoomAsync(int steps) =>
		_ready ? CallAsync("zoom", PlanAddress.Json(writer => writer.WriteNumber("by", steps))) : Task.CompletedTask;

	/// <summary>Back to the whole sheet.</summary>
	public Task FitAsync() =>
		_ready ? CallAsync("fit", null) : Task.CompletedTask;

	/// <summary>
	/// Answers the page's tile requests. Runs on a web view thread (Android: off the UI thread), so it only reads
	/// fields and starts the work; the response streams in when the task completes.
	/// </summary>
	private void OnWebResourceRequested(object? sender, WebViewWebResourceRequestedEventArgs e)
	{
		IPlanTiles? tiles = Volatile.Read(ref _tiles);

		if (tiles is null
			|| !PlanAddress.TryRead(e.Uri, tiles.Spec.Key, out PlanTile tile, out bool image))
		{
			return;
		}

		CancellationToken cancellationToken = _life.Token;

		e.Handled = true;

		var headers =
			new Dictionary<string, string>(Caching, StringComparer.OrdinalIgnoreCase)
			{
				["Content-Type"] = tiles.ContentType
			};

		e.SetResponse(
			200,
			"OK",
			headers,
			image ? tiles.OpenImageAsync(cancellationToken) : tiles.OpenTileAsync(tile, cancellationToken));
	}

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler is null)
		{
			// The page is gone: release the web view and the renderers at once instead of waiting for finalisers.
			if (_web.Handler is not null)
			{
				_web.Handler.DisconnectHandler();
				_ready = false;
			}

			_life.Cancel();
			_life = new CancellationTokenSource();

			Interlocked.Exchange(ref _tiles, null)?.Dispose();

			if (_subscribed)
			{
				Theme.Changed -= OnThemeChanged;
				_subscribed = false;
			}

			return;
		}

		if (!_subscribed)
		{
			Theme.Changed += OnThemeChanged;
			_subscribed = true;
		}
	}

	private async void OnThemeChanged(object? sender, EventArgs e)
	{
		WebBridge.PaintBackground(_web);

		if (_ready)
		{
			await CallAsync("theme", WebBridge.CurrentTheme().ToJson());
		}
	}

	private async void OnRawMessage(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
	{
		string message = e.Message ?? string.Empty;

		try
		{
			if (message == "ready")
			{
				_ready = true;

				if (Volatile.Read(ref _tiles) is { } tiles)
				{
					await PushPlanAsync(tiles);
				}
			}
			else if (message is "shown" or "failed")
			{
				MainThread.BeginInvokeOnMainThread(() => Shown?.Invoke(this, message == "shown"));
			}
			else if (message.StartsWith("zoom:", StringComparison.Ordinal) && message.Length == 8)
			{
				var zoom = new PlanZoom(message[5] == '1', message[7] == '1');

				MainThread.BeginInvokeOnMainThread(() => ZoomChanged?.Invoke(this, zoom));
			}
			else if (message.StartsWith("error:", StringComparison.Ordinal))
			{
				DiagnosticLog.Write($"[Plan JS] {message[6..]}");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Plan] message '{message}' failed: {ex.Message}");
		}
	}

	private Task PushPlanAsync(IPlanTiles tiles) =>
		CallAsync(
			"init",
			PlanAddress.Json(
				writer =>
				{
					writer.WritePropertyName("theme");
					writer.WriteRawValue(WebBridge.CurrentTheme().ToJson());
					writer.WritePropertyName("plan");
					tiles.Spec.Write(writer);
				}));

	private Task CallAsync(string command, string? json) =>
		WebBridge.CallAsync(_web, "ddPlanCall", command, json, "Plan");
}
