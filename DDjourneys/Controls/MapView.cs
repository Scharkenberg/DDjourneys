using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Mapping;
using DDjourneys.Core.Serialization;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// A map: MapLibre GL (vector tiles, embedded in the app under Resources/Raw/wwwroot) in a
/// <see cref="HybridWebView"/>. C# calls the page's <c>ddMapCall</c> through InvokeJavaScriptAsync; the page
/// reports back with raw messages ("ready", "open:url", "error:text"). The basemap is recoloured from the
/// active app theme and needs the user's CARTO key (<see cref="MapAvailability"/>): without a usable one the map is
/// replaced by a quiet notice. Give it a <see cref="MapScene"/>; it keeps the scene and re-applies it when needed.
/// </summary>
public sealed partial class MapView : ContentView
{
	/// <summary>Height of the free strip below the map (see the constructor).</summary>
	public const double StripHeight = 32;

	/// <summary>Preference key: whether the map re-fits itself to its scene ("Auto-fit").</summary>
	private const string AutoFitKey = "MapAutoFit";

	/// <summary>Preference key: the height the user dragged the map to with the grip chin.</summary>
	private const string HeightKey = "MapHeight";

	/// <summary>Height the map starts with when the grip chin can resize it (see <see cref="ResizableByGrip"/>).</summary>
	private const double DefaultHeight = 320;

	/// <summary>Height of the grip chin where it drags: a little taller than the plain strip, still slim.</summary>
	private const double GripHeight = 28;

	/// <summary>The grip chin never drags the map below this: the strip itself and a first look at the scene must fit.</summary>
	private const double MinHeight = 160;

	/// <summary>The share of the window the grip chin can grow the map to: part of the page always stays in reach.</summary>
	private const double WindowShare = 0.9;

	private HybridWebView _web;
	private MapEngine _engine;
	private bool _bootReceived;
	private bool _pageBlocked;
	private string _pageReason = string.Empty;
	private readonly Button _bypassButton;
	private readonly Button _leafletButton;
	private MapScene? _scene;
	private string? _pendingFocus;
	private bool _ready;
	private System.Diagnostics.Stopwatch _created = System.Diagnostics.Stopwatch.StartNew();
	private bool _pageReady;
	private bool _subscribed;
	private readonly Border _notice;
	private readonly Grid _grid;
	private readonly Button _settingsButton;
	private readonly Label _noticeText;
	private readonly Dictionary<string, MapScene> _layers = new(StringComparer.Ordinal);

	// The layer panel's rows, re-pushed when the page (re)boots: they arrive before the map is ready.
	private (string Title, IReadOnlyList<MapLayerRow> Rows)? _layerRows;
	private (bool Explore, bool Pick, double? Latitude, double? Longitude, int Zoom)? _mode;

	// The grip chin: the strip under the map, and the handle that drags its height (see ResizableByGrip).
	private readonly Border _chin;
	private bool _resizableByGrip;
	private bool _draggingHeight;
	private double _pressHeight;

	public MapView()
	{
		_web = CreateWeb();

		// The web view takes every touch (a swipe pans the map), so a strip of plain page background stays below
		// it, inside the view's own bounds: there is always somewhere to put a finger to scroll the page or
		// to go back, on any screen size. Where the map has a frame of its own (ResizableByGrip), the strip is
		// the grip chin as well: the handle that drags the map's height.
		var strip =
			new Border
			{
				HeightRequest = StripHeight,
				BackgroundColor = Colors.Transparent,
				StrokeThickness = 0,
				Content =
					new BoxView
					{
						WidthRequest = 40,
						HeightRequest = 4,
						CornerRadius = 2,
						HorizontalOptions = LayoutOptions.Center,
						VerticalOptions = LayoutOptions.Center,
						Opacity = 0.6
					}
			};

		((BoxView)strip.Content).SetDynamicResource(BoxView.ColorProperty, "Outline");

		_chin = strip;
		_chin.HandlerChanged += OnChinHandlerChanged;

		// Shown instead of the map when there is no (usable) CARTO key: one calm card, no error styling.
		_noticeText =
			new Label
			{
				HorizontalTextAlignment = TextAlignment.Center
			};

		_noticeText.SetDynamicResource(Label.TextColorProperty, "InkMuted");

		_settingsButton =
			new Button
			{
				HorizontalOptions = LayoutOptions.Center,
				Text = LocalizationService.Current.CurrentStrings.Extras.MapKeyOpenSettings,
				StyleClass = ["Tonal"]
			};

		_settingsButton.Clicked += async (_, _) => await Shell.Current.GoToAsync(Routes.Settings);

		_bypassButton =
			new Button
			{
				HorizontalOptions = LayoutOptions.Center,
				Text = LocalizationService.Current.CurrentStrings.Extras.MapBypass,
				StyleClass = ["Tonal"]
			};

		_bypassButton.Clicked += async (_, _) => await BypassAsync();

		_leafletButton =
			new Button
			{
				HorizontalOptions = LayoutOptions.Center,
				Text = LocalizationService.Current.CurrentStrings.Extras.MapUseLeaflet,
				StyleClass = ["Tonal"]
			};

		_leafletButton.Clicked += (_, _) => MapAvailability.SetEngine(MapEngine.Leaflet);

		_notice =
			new Border
			{
				IsVisible = false,
				StrokeThickness = 0,
				Padding = new Thickness(24),
				Content =
					new VerticalStackLayout
					{
						Spacing = 14,
						VerticalOptions = LayoutOptions.Center,
						Children =
						{
							new Icon
							{
								Glyph = IconGlyph.Map,
								Size = 36,
								HorizontalOptions = LayoutOptions.Center
							},
							_noticeText,
							_leafletButton,
							_bypassButton,
							_settingsButton
						}
					}
			};

		_notice.SetDynamicResource(Border.BackgroundColorProperty, "Bg");

		// The web view joins the grid only when the device can draw the map and a key exists (see ApplyAvailability):
		// a web view that is not in the tree never loads its page.
		_grid =
			new Grid
			{
				RowDefinitions =
				[
					new RowDefinition(GridLength.Star),
					new RowDefinition(GridLength.Auto)
				],
				RowSpacing = 0,
				Children =
				{
					_notice,
					strip
				}
			};

		Content = _grid;

		Grid.SetRow(strip, 1);

		ApplyAvailability();

		SemanticProperties.SetDescription(
			this,
			LocalizationService.Current.CurrentStrings.Extras.MapTitle);

		HandlerChanged += OnHandlerChanged;
	}

	/// <summary>A fresh web view: each (re)start of the page gets its own, so no state of an earlier engine is left in it.</summary>
	private HybridWebView CreateWeb()
	{
		var web =
			new HybridWebView
			{
				HorizontalOptions = LayoutOptions.Fill,
				VerticalOptions = LayoutOptions.Fill
			};

		web.RawMessageReceived += OnRawMessage;

		// Logging on: every request the page makes, so a style, a tile or a script that never arrives shows.
		if (DiagnosticLog.Enabled)
		{
			web.WebResourceRequested += (_, e) => DiagnosticLog.Write($"[Map] page requests {e.Uri}");
		}

		_created = System.Diagnostics.Stopwatch.StartNew();
		DiagnosticLog.Write("[Map] web view created");

#if ANDROID
		// The page scales its own text by the OS text size (see MapTheme.FontScale); the web view must not
		// scale it a second time.
		web.HandlerChanged +=
			(_, _) =>
			{
				WebBridge.PaintBackground(web);

				if (web.Handler?.PlatformView is Android.Webkit.WebView platformView)
				{
					platformView.Settings.TextZoom = 100;

					// Logging on: what the device and its web view are, and a look inside the page when it stays silent.
					DDjourneys.Platforms.Android.WebViewDiagnostics.Attach(platformView, "map", () => _pageReady);
				}
			};
#endif

		return web;
	}

	/// <summary>Shows the scene (replacing the previous one). Markers with the same id stay in place.</summary>
	public Task ShowAsync(MapScene scene)
	{
		ArgumentNullException.ThrowIfNull(scene);

		_scene = scene;

		return PushSceneAsync();
	}

	/// <summary>The visible area changed (after a pan or zoom, debounced by the page).</summary>
	public event EventHandler<MapViewport>? ViewportChanged;

	/// <summary>A marker of a layer (see <see cref="SetLayerAsync"/>) was tapped: its id.</summary>
	public event EventHandler<string>? MarkerTapped;

	/// <summary>The map was tapped in pick mode: the point.</summary>
	public event EventHandler<(double Latitude, double Longitude)>? PointTapped;

	/// <summary>A row of the map's layer switch panel; a link row announces its tap instead of toggling.</summary>
	public sealed record MapLayerRow(string Id, string Label, bool Checked, bool Link = false);

	/// <summary>The rows of the map's layer panel were used: their ids and states (false clears a layer).</summary>
	public event EventHandler<IReadOnlyDictionary<string, bool>>? LayersChanged;

	/// <summary>
	/// One layer of the map: markers on top of the scene (stops in the viewport, later live vehicles), filled
	/// areas (tariff zones) and lines - sent once per change, never on every viewport change. Layer ids are
	/// marker-id namespaces ("stops"; Tier 2 adds "parking", "bikes", "vehicles"). An empty scene clears the
	/// layer. Markers of a layer are tappable: a tap raises <see cref="MarkerTapped"/> instead of a popup.
	/// </summary>
	public Task SetLayerAsync(string id, MapScene layer)
	{
		ArgumentNullException.ThrowIfNull(id);

		ArgumentNullException.ThrowIfNull(layer);

		_layers[id] = layer;

		return PushLayerAsync(id);
	}

	/// <summary>
	/// The rows of the map's layer panel (the labels are localised here, the page owns no strings). Without
	/// rows the panel disappears; a link row (no checkbox) posts its id on tap for the host page to act on.
	/// </summary>
	public Task SetLayersAsync(string title, IReadOnlyList<MapLayerRow> rows)
	{
		ArgumentNullException.ThrowIfNull(title);

		ArgumentNullException.ThrowIfNull(rows);

		_layerRows = (title, rows);

		// A node tree, not a reflected dictionary: the app serialises by source-generated metadata only.
		var state = new JsonObject();

		foreach (MapLayerRow row in rows.Where(row => !row.Link))
		{
			state[row.Id] = row.Checked;
		}

		string json =
			new JsonObject
			{
				["label"] = title,
				["rows"] =
					Wire.Array(
						rows.Select(
							row => (JsonNode?)new JsonObject
							{
								["id"] = row.Id,
								["label"] = row.Label,
								["link"] = row.Link
							})),
				["state"] = state
			}.ToJsonString();

		return CallAsync("layers", json);
	}

	/// <summary>
	/// Explore mode follows the viewport (no auto-fit) and pick mode reports taps on the map as points.
	/// A centre moves the view there at once.
	/// </summary>
	public async Task SetModeAsync(bool explore, bool pick, double? latitude = null, double? longitude = null, int zoom = 15)
	{
		_mode = (explore, pick, latitude, longitude, zoom);

		await PushModeAsync();
	}

	/// <summary>Moves the view (animated).</summary>
	public Task CenterAsync(double latitude, double longitude, int zoom = 16) =>
		CallAsync(
			"center",
			string.Create(
				System.Globalization.CultureInfo.InvariantCulture,
				$"{{\"lat\":{latitude},\"lon\":{longitude},\"zoom\":{zoom}}}"));

	/// <summary>Centres the marker with this id and opens its popup.</summary>
	public async Task FocusAsync(string markerId)
	{
		if (!_ready)
		{
			_pendingFocus = markerId;

			return;
		}

		await CallAsync("focus", System.Text.Json.JsonSerializer.Serialize(markerId, WebBridge.StringInfo));
	}

	/// <summary>
	/// The grip chin (the strip under the map) drags the map's height: from <see cref="MinHeight"/> up to most of
	/// the window, starting at <see cref="DefaultHeight"/>; the height the user chose is kept in the preferences.
	/// The page-filling map leaves this off: there the chin keeps its one role, the strip that scrolls the page.
	/// </summary>
	public bool ResizableByGrip
	{
		get => _resizableByGrip;

		set
		{
			_resizableByGrip = value;

			if (value)
			{
				HeightRequest = Math.Max(MinHeight, Preferences.Default.Get(HeightKey, DefaultHeight));

				// The handle is a thumb target, not a hairline: the strip is taller where it can be dragged.
				_chin.HeightRequest = GripHeight;

				SemanticProperties.SetDescription(
					_chin,
					LocalizationService.Current.CurrentStrings.Extras.MapGrip);
			}
		}
	}

	/// <summary>The chin's platform view arrived (or went): the height drag hangs on its native events.</summary>
	private void OnChinHandlerChanged(object? sender, EventArgs e)
	{
#if ANDROID
		if (_chin.Handler?.PlatformView is Android.Views.View view)
		{
			view.SetOnTouchListener(new ChinDragListener(this));
		}
#elif WINDOWS
		AttachChin(_chin.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement);
#endif
	}

	/// <summary>The chin drag began: the height the map had when the finger came down.</summary>
	private void BeginHeightDrag()
	{
		_draggingHeight = true;
		_pressHeight = HeightRequest >= MinHeight ? HeightRequest : Height;

		// The handle answers the touch: it lights up while it is held.
		if (_chin.Content is BoxView bar)
		{
			bar.Opacity = 1;
			bar.WidthRequest = 56;
		}
	}

	/// <summary>The chin moved this far down since the drag began (negative: up).</summary>
	private void MoveHeightDrag(double distance)
	{
		if (!_draggingHeight)
		{
			return;
		}

		HeightRequest = Math.Clamp(_pressHeight + distance, MinHeight, HeightCeiling());
	}

	/// <summary>The chin drag ended: the height the user chose stays for the next time.</summary>
	private void EndHeightDrag()
	{
		if (!_draggingHeight)
		{
			return;
		}

		_draggingHeight = false;

		if (_chin.Content is BoxView bar)
		{
			bar.Opacity = 0.6;
			bar.WidthRequest = 40;
		}

		Preferences.Default.Set(HeightKey, HeightRequest);
	}

	/// <summary>The largest height the chin may drag the map to: most of the window, so part of the page stays in reach.</summary>
	private double HeightCeiling()
	{
		for (Element? element = Parent; element is not null; element = element.Parent)
		{
			if (element is Page page
				&& page.Window?.Height is { } height
				&& height > 0)
			{
				return Math.Max(MinHeight, height * WindowShare);
			}
		}

		return Math.Max(MinHeight, DefaultHeight * WindowShare);
	}

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler is null)
		{
			// The page is gone: release the native web view at once instead of waiting for its finaliser. Should the
			// view ever be attached again it gets a new handler and the page boots from the start.
			if (_web.Handler is not null)
			{
				_web.Handler.DisconnectHandler();
				_pageReady = false;
				_ready = false;
				_bootReceived = false;
			}

			if (_subscribed)
			{
				// Left in an orderly way: not a crash.
				MapSupport.Finish();

				MapAvailability.Changed -= OnAvailabilityChanged;
				Theme.Changed -= OnThemeChanged;
				SystemAccessibility.Changed -= OnThemeChanged;
				_subscribed = false;
			}

			return;
		}

		if (!_subscribed)
		{
			MapAvailability.Changed += OnAvailabilityChanged;
			Theme.Changed += OnThemeChanged;
			SystemAccessibility.Changed += OnThemeChanged;
			_subscribed = true;
		}
	}

	private async void OnRawMessage(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
	{
		string message = e.Message ?? string.Empty;

		// A web view that was replaced (another engine) may still say something.
		if (!ReferenceEquals(sender, _web))
		{
			return;
		}

		try
		{
			if (message == "boot")
			{
				// The page is up and waits for the engine: the choice is the app's.
				_bootReceived = true;

				await StartPageAsync(MapSupport.Bypassed);
			}
			else if (message == "ready")
			{
				_pageReady = true;

				MapSupport.Finish();

				DiagnosticLog.Write($"[Map] page ready {_created.ElapsedMilliseconds} ms after the web view was created; map available: {MapAvailability.IsAvailable}");

				if (MapAvailability.IsAvailable)
				{
					await InitializeAsync();
				}
			}
			else if (message.StartsWith("unsupported:", StringComparison.Ordinal))
			{
				// The page's own check (WebGL 2, language features) failed before MapLibre was loaded.
				_pageBlocked = true;
				_pageReason = message[12..];

				DiagnosticLog.Write($"[Map] the page cannot draw the map: {_pageReason}");

				MapSupport.Finish();

				await MainThread.InvokeOnMainThreadAsync(ApplyAvailability);
			}
			else if (message == "keyrejected")
			{
				// CARTO refused the key: said once, the map is replaced by the notice until the key changes.
				MapAvailability.Reject();
			}
			else if (message.StartsWith("open:", StringComparison.Ordinal)
				&& Uri.TryCreate(message[5..], UriKind.Absolute, out Uri? uri)
				&& uri.Scheme is "http" or "https")
			{
				await Launcher.Default.OpenAsync(uri);
			}
			else if (message.StartsWith("view:", StringComparison.Ordinal))
			{
				RaiseViewport(message[5..]);
			}
			else if (message.StartsWith("tap:", StringComparison.Ordinal))
			{
				string id = message[4..];

				await MainThread.InvokeOnMainThreadAsync(() => MarkerTapped?.Invoke(this, id));
			}
			else if (message.StartsWith("point:", StringComparison.Ordinal)
				&& message[6..].Split(',') is [var lat, var lon]
				&& double.TryParse(lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double latitude)
				&& double.TryParse(lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double longitude))
			{
				await MainThread.InvokeOnMainThreadAsync(() => PointTapped?.Invoke(this, (latitude, longitude)));
			}
			else if (message.StartsWith("auto:", StringComparison.Ordinal))
			{
				Preferences.Default.Set(AutoFitKey, message == "auto:true");
			}
			else if (message.StartsWith("layers:", StringComparison.Ordinal))
			{
				// The layer panel was used: every row's state, so the page host stays stateless.
				TryRaiseLayersChanged(message[7..]);
			}
			else if (message.StartsWith("error:", StringComparison.Ordinal))
			{
				DiagnosticLog.Write($"[Map JS] {message[6..]}");
			}
			else if (message.StartsWith("log:", StringComparison.Ordinal))
			{
				// The page's own diagnostics (engine, language features, WebGL, errors with stack, map events).
				DiagnosticLog.Write($"[Map page] {message[4..]}");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] message '{message}' failed: {ex.Message}");
		}
	}

	private async Task InitializeAsync()
	{
		string key = MapAvailability.Key;

		ExtrasStrings strings = LocalizationService.Current.CurrentStrings.Extras;

		DiagnosticLog.Write($"[Map] sending init ({_created.ElapsedMilliseconds} ms)");

		await CallAsync(
			"init",
			WebBridge.CurrentTheme().ToInitJson(
				key,
				Preferences.Default.Get(AutoFitKey, true),
				strings.MapAutoFit,
				strings.MapFitNow,
				strings.MapInfo));

		_ready = true;

		DiagnosticLog.Write($"[Map] init answered ({_created.ElapsedMilliseconds} ms), pushing scene, mode and layers");

		await PushSceneAsync();
		await PushModeAsync();
		await PushLayersAsync();

		if (_layerRows is { } panel)
		{
			await SetLayersAsync(panel.Title, panel.Rows);
		}

		if (_pendingFocus is { } id)
		{
			_pendingFocus = null;

			await FocusAsync(id);
		}
	}

	private void OnAvailabilityChanged(object? sender, EventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			async () =>
			{
				ApplyAvailability();

				// A key arrived after the page was loaded: the map starts now.
				if (!MapAvailability.IsAvailable
					|| !_pageReady)
				{
					return;
				}

				if (!_ready)
				{
					await InitializeAsync();
				}
				else
				{
					// A different key: the page draws the basemap again with it.
					await CallAsync(
						"key",
						$"{{\"key\":{System.Text.Json.JsonSerializer.Serialize(MapAvailability.Key, WebBridge.StringInfo)}}}");
				}
			});

	/// <summary>Tells the page which engine to start (and whether to ignore its own checks).</summary>
	private Task StartPageAsync(bool force) =>
		WebBridge.CallAsync(
			_web,
			"ddBoot",
			"start",
			$"{{\"engine\":\"{(_engine == MapEngine.Leaflet ? MapAvailability.LeafletId : MapAvailability.CartoId)}\",\"force\":{(force ? "true" : "false")}}}",
			"Map");

	/// <summary>"Try anyway": the checks stop applying; the page that is already loaded is told to go on, else the web view is created now.</summary>
	private async Task BypassAsync()
	{
		MapSupport.Bypass();

		_pageBlocked = false;

		bool attached = _grid.Children.Contains(_web);

		ApplyAvailability();

		if (attached
			&& _bootReceived)
		{
			await StartPageAsync(true);
		}
	}

	/// <summary>
	/// The map when it can be drawn (the engine's needs are met: for CARTO a key and a device that passes the check,
	/// for Leaflet nothing), else the notice that says why and what can be done. A change of engine starts a new web view.
	/// </summary>
	private void ApplyAvailability()
	{
		MapEngine engine = MapAvailability.Engine;
		bool carto = engine == MapEngine.Carto;
		MapBlock block = carto ? MapSupport.Block : MapBlock.None;
		bool wanted = block == MapBlock.None && MapAvailability.IsAvailable;
		bool attached = _grid.Children.Contains(_web);

		if (attached
			&& (!wanted || engine != _engine))
		{
			DiagnosticLog.Write($"[Map] web view removed ({(wanted ? "engine changed" : "map not wanted")})");

			_grid.Children.Remove(_web);
			_web.RawMessageReceived -= OnRawMessage;
			MapSupport.Finish();

			// A removed web view keeps its native peer until collection: let it go here.
			_web.Handler?.DisconnectHandler();

			_web = CreateWeb();
			_pageReady = false;
			_ready = false;
			_bootReceived = false;
			_pageBlocked = false;
			attached = false;
		}

		if (wanted
			&& !attached)
		{
			DiagnosticLog.Write($"[Map] loading the map page ({engine})");

			_engine = engine;

			if (carto)
			{
				MapSupport.Begin();
			}

			_grid.Children.Insert(0, _web);
		}
		else if (!wanted)
		{
			DiagnosticLog.Write($"[Map] map not started: {(block == MapBlock.None ? "no usable key" : MapSupport.Detail)}");
		}

		bool shown = wanted && !_pageBlocked;

		_notice.IsVisible = !shown;

		ExtrasStrings strings = LocalizationService.Current.CurrentStrings.Extras;

		_noticeText.Text =
			_pageBlocked || block == MapBlock.Device
				? strings.MapUnsupported
				: block == MapBlock.Crashed
					? strings.MapCrashed
					: MapAvailability.HasKey ? strings.MapKeyInvalid : strings.MapKeyMissing;

		// What can be done about it: try the CARTO map anyway (it may work, or end the app: that is remembered), use
		// Leaflet, or look at the settings.
		_bypassButton.IsVisible = carto && (_pageBlocked || block != MapBlock.None);
		_leafletButton.IsVisible = carto;
	}

	private async void OnThemeChanged(object? sender, EventArgs e)
	{
		WebBridge.PaintBackground(_web);

		if (!_ready)
		{
			return;
		}

		await CallAsync("theme", WebBridge.CurrentTheme().ToJson());

		// Routes and markers carry theme colours too: the same scene again, in the new colours, without moving the view.
		if (_scene is { } scene)
		{
			await CallAsync("set", scene.ToJson(Theme.IsDark, MapScenes.Resolve, fit: false));
		}

		await PushLayersAsync();
	}

	private void TryRaiseLayersChanged(string json)
	{
		try
		{
			var states = new Dictionary<string, bool>(StringComparer.Ordinal);

			using (JsonDocument document = JsonDocument.Parse(json))
			{
				foreach (JsonProperty property in document.RootElement.EnumerateObject())
				{
					states[property.Name] = property.Value.ValueKind == JsonValueKind.True;
				}
			}

			MainThread.BeginInvokeOnMainThread(() => LayersChanged?.Invoke(this, states));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] layer states '{json}' failed: {ex.Message}");
		}
	}

	// The page reports on a background thread; listeners touch views, so they are called on the UI thread.
	private void RaiseViewport(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		var viewport =
			new MapViewport(
				root.GetProperty("s").GetDouble(),
				root.GetProperty("w").GetDouble(),
				root.GetProperty("n").GetDouble(),
				root.GetProperty("e").GetDouble(),
				root.GetProperty("z").GetDouble(),
				root.GetProperty("lat").GetDouble(),
				root.GetProperty("lon").GetDouble());

		MainThread.BeginInvokeOnMainThread(() => ViewportChanged?.Invoke(this, viewport));
	}

	private async Task PushLayerAsync(string id)
	{
		if (!_ready
			|| !_layers.TryGetValue(id, out MapScene? layer))
		{
			return;
		}

		await CallAsync("layer", id, layer.ToJson(Theme.IsDark, MapScenes.Resolve));
	}

	private async Task PushLayersAsync()
	{
		foreach (string id in _layers.Keys)
		{
			await PushLayerAsync(id);
		}
	}

	private async Task PushModeAsync()
	{
		if (!_ready
			|| _mode is not { } mode)
		{
			return;
		}

		string center =
			mode.Latitude is { } lat && mode.Longitude is { } lon
				? string.Create(
					System.Globalization.CultureInfo.InvariantCulture,
					$",\"center\":{{\"lat\":{lat},\"lon\":{lon},\"zoom\":{mode.Zoom}}}")
				: string.Empty;

		await CallAsync(
			"mode",
			$"{{\"explore\":{(mode.Explore ? "true" : "false")},\"pick\":{(mode.Pick ? "true" : "false")}{center}}}");

		// The mode is applied once; a later re-send must not move the view again.
		_mode = (mode.Explore, mode.Pick, null, null, mode.Zoom);
	}

	private async Task PushSceneAsync()
	{
		if (!_ready
			|| _scene is not { } scene)
		{
			return;
		}

		await CallAsync("set", scene.ToJson(Theme.IsDark, MapScenes.Resolve));
	}

	private Task CallAsync(string command, string? json) =>
		WebBridge.CallAsync(_web, "ddMapCall", command, json, "Map");

	private Task CallAsync(string command, string argument, string? json) =>
		WebBridge.CallAsync(_web, "ddMapCall", command, argument, json, "Map");

#if WINDOWS
	private Microsoft.UI.Xaml.UIElement? _chinView;
	private double _pressY;

	// The chin's pointer events carry the drag: the pointer is captured at once, so the scroll view around
	// the page cannot pan it away while the height follows the pointer.
	private void AttachChin(Microsoft.UI.Xaml.UIElement? view)
	{
		if (ReferenceEquals(view, _chinView))
		{
			return;
		}

		if (_chinView is not null)
		{
			_chinView.PointerPressed -= OnChinPressed;
			_chinView.PointerMoved -= OnChinMoved;
			_chinView.PointerReleased -= OnChinReleased;
			_chinView.PointerCaptureLost -= OnChinCaptureLost;
		}

		_chinView = view;

		if (view is null)
		{
			return;
		}

		view.PointerPressed += OnChinPressed;
		view.PointerMoved += OnChinMoved;
		view.PointerReleased += OnChinReleased;
		view.PointerCaptureLost += OnChinCaptureLost;
	}

	private void OnChinPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (_chinView is not { } view
			|| !_resizableByGrip
			|| _draggingHeight
			|| !e.GetCurrentPoint(view).Properties.IsLeftButtonPressed)
		{
			return;
		}

		e.Handled = true;

		view.CapturePointer(e.Pointer);

		// The window's coordinates, not the chin's own: the chin moves with the drag, so a position measured
		// from it would stand still under the finger and the height would never follow.
		_pressY = e.GetCurrentPoint(null).Position.Y;

		BeginHeightDrag();
	}

	private void OnChinMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (_chinView is not { } view
			|| !_draggingHeight)
		{
			return;
		}

		e.Handled = true;

		MoveHeightDrag(e.GetCurrentPoint(null).Position.Y - _pressY);
	}

	private void OnChinReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (_chinView is not { } view
			|| !_draggingHeight)
		{
			return;
		}

		e.Handled = true;

		view.ReleasePointerCapture(e.Pointer);

		EndHeightDrag();
	}

	private void OnChinCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
		EndHeightDrag();
#endif

#if ANDROID
	/// <summary>
	/// The chin's touch listener: it takes the whole drag (the scroll view around the page would claim a
	/// vertical drag of a child before any recognizer sees it) and keeps the parents from intercepting while
	/// the finger is down. Without the drag (the page-filling map) it stays out of the way: the chin scrolls
	/// the page as before.
	/// </summary>
	private sealed class ChinDragListener : Java.Lang.Object, Android.Views.View.IOnTouchListener
	{
		private readonly MapView _map;
		private float _downY;
		private float _density = 1;

		public ChinDragListener(MapView map) => _map = map;

		public bool OnTouch(Android.Views.View? view, Android.Views.MotionEvent? motion)
		{
			if (view is null
				|| motion is null
				|| !_map.ResizableByGrip)
			{
				return false;
			}

			switch (motion.ActionMasked)
			{
				case Android.Views.MotionEventActions.Down:
					// As long as the finger is down, no parent may intercept the drag.
					view.Parent?.RequestDisallowInterceptTouchEvent(true);

					// Screen coordinates: the chin moves with the drag, a position relative to it would not change.
					_downY = motion.RawY;
					_density = view.Resources?.DisplayMetrics?.Density ?? 1;

					_map.BeginHeightDrag();

					return true;

				case Android.Views.MotionEventActions.Move:
					_map.MoveHeightDrag((motion.RawY - _downY) / _density);

					return true;

				case Android.Views.MotionEventActions.Up:
				case Android.Views.MotionEventActions.Cancel:
					view.Parent?.RequestDisallowInterceptTouchEvent(false);

					_map.EndHeightDrag();

					return true;

				default:
					return true;
			}
		}
	}
#endif
}
