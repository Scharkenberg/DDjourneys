using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>Whether the plan can be zoomed further in and out (for the page's zoom buttons).</summary>
public readonly record struct PlanZoom(bool CanZoomIn, bool CanZoomOut);

/// <summary>A stop of the plan: its id, its place on the sheet in plan pixels of the deepest level, and its label.</summary>
public sealed record PlanStop(string Id, double X, double Y, string Title);

/// <summary>The marks the host can place on a stop of the plan.</summary>
public enum PlanMark
{
	/// <summary>Where a journey starts.</summary>
	From,

	/// <summary>Where it ends.</summary>
	To,

	/// <summary>The stop nearest to the passenger.</summary>
	Here,

	/// <summary>The stop picked to be looked at.</summary>
	Focus
}

/// <summary>What the view does after a mark was placed.</summary>
public enum PlanFit
{
	/// <summary>Nothing; the view stays as it is.</summary>
	None,

	/// <summary>Every mark that stands, in view.</summary>
	All,

	/// <summary>This mark centred, close enough to read it and its neighbours.</summary>
	Center
}

/// <summary>
/// Shows a plan of any size (a network map, a fare zone plan) in a <see cref="HybridWebView"/> with Leaflet
/// (Resources/Raw/wwwroot/plan.html): pinch, pan with inertia, double tap, wheel and keys, clamped to the sheet.
/// The page asks for <c>/_plan/...</c> on its own origin; those requests are answered here from the
/// <see cref="IPlanTiles"/> (<see cref="HybridWebView.WebResourceRequested"/>), so tiles come from the device,
/// whatever made them. The view owns the tiles it is given and disposes them when replaced or gone.
/// </summary>
public sealed partial class PlanView : ContentView
{
	// Never kept by the web view: the status is sent before the tile is drawn, so a tile that failed is an empty 200,
	// and a cached empty answer would stay a hole. The tiles live in the app's own disk cache instead.
	private static readonly IReadOnlyDictionary<string, string> Caching =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Cache-Control"] = "no-store" };

	private readonly HybridWebView _web;
	private IPlanTiles? _tiles;
	private CancellationTokenSource _life = new();
	private bool _ready;
	private bool _hadPlan;
	private bool _subscribed;
	private IReadOnlyList<PlanStop>? _stops;
	private double _stopScale;
	private Dictionary<PlanMark, (string? Id, string? Title, PlanFit Fit)>? _marks;

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

	/// <summary>
	/// The web view was rebuilt (its handler went and came back) and the plan it showed is gone with the old
	/// instance: the host may show one again. Not raised for a view that never had a plan.
	/// </summary>
	public event EventHandler? PlanLost;

	/// <summary>A stop of the plan was tapped (its id); the plan page marks it focused in the web view itself.</summary>
	public event EventHandler<string>? StopTapped;

	/// <summary>Shows a plan; the view takes ownership of <paramref name="tiles"/> (the previous plan is disposed).</summary>
	public Task ShowAsync(IPlanTiles tiles)
	{
		ArgumentNullException.ThrowIfNull(tiles);

		_hadPlan = true;

		// A plan shown anew starts empty: its stops and marks are the page's to send again.
		_stops = null;
		_marks = null;

		IPlanTiles? previous = Interlocked.Exchange(ref _tiles, tiles);

		if (!ReferenceEquals(previous, tiles))
		{
			previous?.Dispose();
		}

		return _ready ? PushPlanAsync(tiles) : Task.CompletedTask;
	}

	/// <summary>
	/// The stops of the plan, tap targets drawn on the sheet from <paramref name="tapScale"/> on (CSS pixels per plan
	/// pixel; below it a tap zooms in towards them instead). Replaces the stops sent before; they are kept while the
	/// page is not ready yet and sent once it is. A plan shown after them starts without stops.
	/// </summary>
	public Task SetStopsAsync(IReadOnlyList<PlanStop> stops, double tapScale)
	{
		ArgumentNullException.ThrowIfNull(stops);

		_stops = stops;
		_stopScale = tapScale;

		return _ready ? PushStopsAsync() : Task.CompletedTask;
	}

	/// <summary>
	/// Places the mark of one kind on a stop (a null id takes it away) and moves the view as <paramref name="fit"/>
	/// asks. The marks are kept while the page is not ready yet and replayed once it is; a plan shown after them
	/// starts without marks.
	/// </summary>
	public Task MarkAsync(PlanMark mark, string? id, string? title, PlanFit fit = PlanFit.None)
	{
		(_marks ??= new())[mark] = (id, title, fit);

		return _ready ? PushMarkAsync(mark, id, title, fit) : Task.CompletedTask;
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
			// A plan address that is nobody's (no plan, or another plan's key) becomes a 404 in the page: said once per kind.
			if (e.Uri.AbsolutePath.StartsWith(PlanAddress.Prefix, StringComparison.Ordinal))
			{
				DiagnosticLog.Write($"[Plan] request not answered ({(tiles is null ? "no plan" : "other key than " + tiles.Spec.Key)}): {e.Uri.AbsolutePath}");
			}

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
			Logged(image ? tiles.OpenImageAsync(cancellationToken) : tiles.OpenTileAsync(tile, cancellationToken), e.Uri.AbsolutePath));
	}

	private int _answered;

	/// <summary>Passes the answer on; the first ones, and every one that came back empty or failed, go to the log.</summary>
	private async Task<Stream?> Logged(Task<Stream?> answer, string path)
	{
		try
		{
			Stream? stream = await answer.ConfigureAwait(false);

			if (stream is null || Interlocked.Increment(ref _answered) <= 4)
			{
				DiagnosticLog.Write($"[Plan] {path}: {(stream is null ? "nothing" : stream.CanSeek ? stream.Length + " bytes" : "a stream")}");
			}

			return stream;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Plan] {path} failed: {ex.Message}");

			throw;
		}
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
			_stops = null;
			_marks = null;

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
					await PushStopsAsync();
					await PushMarksAsync();
				}
				else if (_hadPlan)
				{
					// The old instance took its tiles with it; only the host can bring a plan back.
					MainThread.BeginInvokeOnMainThread(() => PlanLost?.Invoke(this, EventArgs.Empty));
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
			else if (message.StartsWith("pin:", StringComparison.Ordinal))
			{
				// The page marks the stop focused in the web view itself ('unpin', the letting go of that mark, needs
				// no C# side yet).
				string id = message[4..];

				MainThread.BeginInvokeOnMainThread(() => StopTapped?.Invoke(this, id));
			}
			else if (message.StartsWith("log:", StringComparison.Ordinal))
			{
				DiagnosticLog.Write($"[Plan JS] {message[4..]}");
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

	private Task PushStopsAsync() =>
		_stops is { } stops
			? CallAsync(
				"stops",
				PlanAddress.Json(
					writer =>
					{
						writer.WriteNumber("minScale", _stopScale);
						writer.WriteStartArray("pins");

						foreach (PlanStop stop in stops)
						{
							writer.WriteStartArray();
							writer.WriteStringValue(stop.Id);
							writer.WriteNumberValue(stop.X);
							writer.WriteNumberValue(stop.Y);
							writer.WriteStringValue(stop.Title);
							writer.WriteEndArray();
						}

						writer.WriteEndArray();
					}))
			: Task.CompletedTask;

	/// <summary>The marks in the order they were placed: the end state is that of placing them one by one.</summary>
	private async Task PushMarksAsync()
	{
		if (_marks is null)
		{
			return;
		}

		foreach (KeyValuePair<PlanMark, (string? Id, string? Title, PlanFit Fit)> placed in _marks)
		{
			await PushMarkAsync(placed.Key, placed.Value.Id, placed.Value.Title, placed.Value.Fit);
		}
	}

	private Task PushMarkAsync(PlanMark mark, string? id, string? title, PlanFit fit) =>
		CallAsync(
			"mark",
			PlanAddress.Json(
				writer =>
				{
					writer.WriteString("kind", KindOf(mark));

					if (id is { } stopId)
					{
						writer.WriteString("id", stopId);

						if (title is { } label)
						{
							writer.WriteString("label", label);
						}
					}

					if (fit != PlanFit.None)
					{
						writer.WriteString("fit", fit == PlanFit.All ? "all" : "center");
					}
				}));

	private static string KindOf(PlanMark mark) =>
		mark switch
		{
			PlanMark.From => "from",
			PlanMark.To => "to",
			PlanMark.Here => "here",
			_ => "focus"
		};

	private Task CallAsync(string command, string? json) =>
		WebBridge.CallAsync(_web, "ddPlanCall", command, json, "Plan");
}
