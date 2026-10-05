using System.Text.Json;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Mapping;
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

	private readonly HybridWebView _web;
	private MapScene? _scene;
	private string? _pendingFocus;
	private bool _ready;
	private bool _pageReady;
	private bool _subscribed;
	private readonly Border _notice;
	private readonly Label _noticeText;
	private MapScene? _overlay;
	private (bool Explore, bool Pick, double? Latitude, double? Longitude, int Zoom)? _mode;

	public MapView()
	{
		_web =
			new HybridWebView
			{
				HorizontalOptions = LayoutOptions.Fill,
				VerticalOptions = LayoutOptions.Fill
			};

		_web.RawMessageReceived += OnRawMessage;

#if ANDROID
		// The page scales its own text by the OS text size (see MapTheme.FontScale); the web view must not
		// scale it a second time.
		_web.HandlerChanged +=
			(_, _) =>
			{
				WebBridge.PaintBackground(_web);

				if (_web.Handler?.PlatformView is Android.Webkit.WebView platformView)
				{
					platformView.Settings.TextZoom = 100;
				}
			};
#endif

		// The web view takes every touch (a swipe pans the map), so a strip of plain page background stays below
		// it, inside the view's own bounds: there is always somewhere to put a finger to scroll the page or
		// to go back, on any screen size.
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

		// Shown instead of the map when there is no (usable) CARTO key: one calm card, no error styling.
		_noticeText =
			new Label
			{
				HorizontalTextAlignment = TextAlignment.Center
			};

		_noticeText.SetDynamicResource(Label.TextColorProperty, "InkMuted");

		var settings =
			new Button
			{
				HorizontalOptions = LayoutOptions.Center,
				Text = LocalizationService.Current.CurrentStrings.Extras.MapKeyOpenSettings,
				StyleClass = ["Tonal"]
			};

		settings.Clicked += async (_, _) => await Shell.Current.GoToAsync(Routes.Settings);

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
							settings
						}
					}
			};

		_notice.SetDynamicResource(Border.BackgroundColorProperty, "Bg");

		Content =
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
					_web,
					_notice,
					strip
				}
			};

		Grid.SetRow(strip, 1);

		ApplyAvailability();

		SemanticProperties.SetDescription(
			this,
			LocalizationService.Current.CurrentStrings.Extras.MapTitle);

		HandlerChanged += OnHandlerChanged;
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

	/// <summary>A marker of the overlay (see <see cref="SetOverlayAsync"/>) was tapped: its id.</summary>
	public event EventHandler<string>? MarkerTapped;

	/// <summary>The map was tapped in pick mode: the point.</summary>
	public event EventHandler<(double Latitude, double Longitude)>? PointTapped;

	/// <summary>
	/// Markers on top of the scene that the page loads itself (stops in the viewport). They are tappable: a tap raises
	/// <see cref="MarkerTapped"/> instead of opening a popup.
	/// </summary>
	public Task SetOverlayAsync(MapScene overlay)
	{
		ArgumentNullException.ThrowIfNull(overlay);

		_overlay = overlay;

		return PushOverlayAsync();
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

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler is null)
		{
			if (_subscribed)
			{
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

		try
		{
			if (message == "ready")
			{
				_pageReady = true;

				if (MapAvailability.IsAvailable)
				{
					await InitializeAsync();
				}
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
			else if (message.StartsWith("error:", StringComparison.Ordinal))
			{
				DiagnosticLog.Write($"[Map JS] {message[6..]}");
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

		await CallAsync(
			"init",
			WebBridge.CurrentTheme().ToInitJson(
				key,
				Preferences.Default.Get(AutoFitKey, true),
				strings.MapAutoFit,
				strings.MapFitNow,
				strings.MapInfo));

		_ready = true;

		await PushSceneAsync();
		await PushModeAsync();
		await PushOverlayAsync();

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

	/// <summary>The map when there is a usable key, else the notice that says what to do.</summary>
	private void ApplyAvailability()
	{
		bool available = MapAvailability.IsAvailable;

		_web.IsVisible = available;
		_notice.IsVisible = !available;

		ExtrasStrings strings = LocalizationService.Current.CurrentStrings.Extras;

		_noticeText.Text =
			MapAvailability.HasKey
				? strings.MapKeyInvalid
				: strings.MapKeyMissing;
	}

	private async void OnThemeChanged(object? sender, EventArgs e)
	{
		WebBridge.PaintBackground(_web);

		if (!_ready)
		{
			return;
		}

		await CallAsync("theme", WebBridge.CurrentTheme().ToJson());
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

	private async Task PushOverlayAsync()
	{
		if (!_ready
			|| _overlay is not { } overlay)
		{
			return;
		}

		await CallAsync("overlay", overlay.ToJson(Theme.IsDark));
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

		await CallAsync("set", scene.ToJson(Theme.IsDark));
	}

	private Task CallAsync(string command, string? json) =>
		WebBridge.CallAsync(_web, "ddMapCall", command, json, "Map");
}
