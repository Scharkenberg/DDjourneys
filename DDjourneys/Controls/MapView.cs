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
/// active app theme. Give it a <see cref="MapScene"/>; it keeps the scene and re-applies it when needed.
/// </summary>
public sealed class MapView : ContentView
{
	private static string? _cartoKey;

	/// <summary>Height of the free strip below the map (see the constructor).</summary>
	public const double StripHeight = 32;

	/// <summary>Preference key: whether the map re-fits itself to its scene ("Auto-fit").</summary>
	private const string AutoFitKey = "MapAutoFit";

	private readonly HybridWebView _web;
	private MapScene? _scene;
	private string? _pendingFocus;
	private bool _ready;
	private bool _subscribed;

	public MapView()
	{
		_web =
			new HybridWebView
			{
				HorizontalOptions = LayoutOptions.Fill,
				VerticalOptions = LayoutOptions.Fill
			};

		_web.RawMessageReceived += OnRawMessage;

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
					strip
				}
			};

		Grid.SetRow(strip, 1);

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

	/// <summary>Centres the marker with this id and opens its popup.</summary>
	public async Task FocusAsync(string markerId)
	{
		if (!_ready)
		{
			_pendingFocus = markerId;

			return;
		}

		await CallAsync("focus", JsonSerializer.Serialize(markerId));
	}

	private void OnHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler is null)
		{
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

	private async void OnRawMessage(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
	{
		string message = e.Message ?? string.Empty;

		try
		{
			if (message == "ready")
			{
				await InitializeAsync();
			}
			else if (message.StartsWith("open:", StringComparison.Ordinal)
				&& Uri.TryCreate(message[5..], UriKind.Absolute, out Uri? uri)
				&& uri.Scheme is "http" or "https")
			{
				await Launcher.Default.OpenAsync(uri);
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
		string key = _cartoKey ??= await ReadCartoKeyAsync();

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

		if (_pendingFocus is { } id)
		{
			_pendingFocus = null;

			await FocusAsync(id);
		}
	}

	// The CARTO key lives in Resources/Raw/secrets.json, which is not in the repository (.gitignore) and,
	// being outside wwwroot, is never served to the page. Without it the map still tries CARTO's keyless access.
	private static async Task<string> ReadCartoKeyAsync()
	{
		try
		{
			using Stream stream = await FileSystem.OpenAppPackageFileAsync("secrets.json");
			using JsonDocument document = await JsonDocument.ParseAsync(stream);

			if (document.RootElement.TryGetProperty("CartoApiKey", out JsonElement value)
				&& value.ValueKind == JsonValueKind.String
				&& value.GetString()?.Trim() is { Length: > 0 } key
				&& !key.StartsWith("PASTE", StringComparison.OrdinalIgnoreCase))
			{
				DiagnosticLog.Write("[Map] CARTO key loaded from secrets.json");

				return key;
			}

			DiagnosticLog.Write("[Map] secrets.json has no CartoApiKey; trying CARTO without a key");
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] no secrets.json ({ex.GetType().Name}); trying CARTO without a key");
		}

		return string.Empty;
	}

	private async void OnThemeChanged(object? sender, EventArgs e)
	{
		if (!_ready)
		{
			return;
		}

		await CallAsync("theme", WebBridge.CurrentTheme().ToJson());
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
