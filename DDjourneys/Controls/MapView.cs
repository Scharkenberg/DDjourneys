using System.Text.Json;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Mapping;
using DDjourneys.Localization;

namespace DDjourneys.Controls;

/// <summary>
/// A map: Leaflet (embedded in the app) running in a web view, with free OpenStreetMap-based tiles.
/// Needs no API key and no native map package. Give it a <see cref="MapScene"/>; it keeps the scene and
/// re-applies it when the page finished loading or the app theme changed.
/// </summary>
public sealed class MapView : ContentView
{
	// A real https origin, so the page is not an anonymous "about:blank" document.
	private const string Origin = "https://github.com/Scharkenberg/DDjourneys";

	private static string? _html;

	private readonly WebView _web;
	private MapScene? _scene;
	private string? _pendingFocus;
	private bool _ready;
	private bool _loading;

	public MapView()
	{
		_web =
			new WebView
			{
				HorizontalOptions = LayoutOptions.Fill,
				VerticalOptions = LayoutOptions.Fill
			};

		_web.Navigating += OnNavigating;
		_web.Navigated += OnNavigated;

		Content = _web;

		SemanticProperties.SetDescription(
			this,
			LocalizationService.Current.CurrentStrings.Extras.MapTitle);

		HandlerChanged += OnHandlerChanged;
	}

	private static bool IsDark =>
		Application.Current is { } app
		&& (app.UserAppTheme != AppTheme.Unspecified
			? app.UserAppTheme
			: app.RequestedTheme) == AppTheme.Dark;

	/// <summary>Shows the scene (replacing the previous one). Markers with the same id move instead of blinking.</summary>
	public Task ShowAsync(MapScene scene)
	{
		ArgumentNullException.ThrowIfNull(scene);

		_scene = scene;

		return PushAsync();
	}

	/// <summary>Centres the marker with this id and opens its popup.</summary>
	public async Task FocusAsync(string markerId)
	{
		if (!_ready)
		{
			_pendingFocus = markerId;

			return;
		}

		await EvaluateAsync($"ddMap.focus({System.Text.Json.JsonSerializer.Serialize(markerId)});");
	}

	private async void OnHandlerChanged(object? sender, EventArgs e)
	{
		if (Handler is null)
		{
			if (Application.Current is { } old)
			{
				old.RequestedThemeChanged -= OnThemeChanged;
			}

			return;
		}

		Application.Current!.RequestedThemeChanged -= OnThemeChanged;
		Application.Current!.RequestedThemeChanged += OnThemeChanged;

		if (_ready || _loading)
		{
			return;
		}

		_loading = true;

		try
		{
			_web.Source =
				new HtmlWebViewSource
				{
					Html = await BuildHtmlAsync(),
					BaseUrl = Origin
				};
		}
		catch (Exception ex)
		{
			_loading = false;

			System.Diagnostics.Debug.WriteLine($"Map page could not be built: {ex}");
		}
	}

	private static async Task<string> BuildHtmlAsync()
	{
		if (_html is not null)
		{
			return _html;
		}

		static async Task<string> ReadAsync(string name)
		{
			using Stream stream = await FileSystem.OpenAppPackageFileAsync(name);
			using var reader = new StreamReader(stream);

			return await reader.ReadToEndAsync();
		}

		string template = await ReadAsync("ddmap.html");
		string css = await ReadAsync("leaflet.css");
		string script = await ReadAsync("leaflet.js");
		string key = await ReadCartoKeyAsync();

		return _html =
			template
				.Replace("/*LEAFLET_CSS*/", css, StringComparison.Ordinal)
				.Replace("/*LEAFLET_JS*/", script, StringComparison.Ordinal)
				.Replace(
					"/*CARTO_QUERY*/",
					key.Length == 0
						? string.Empty
						: "?key=" + Uri.EscapeDataString(key),
					StringComparison.Ordinal);
	}

	// The CARTO key lives in Resources/Raw/secrets.json, which is not in the repository (.gitignore).
	// Without it the map still loads, but CARTO draws its "API KEY REQUIRED" watermark.
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
				return key;
			}

			DiagnosticLog.Write("[Map] secrets.json has no CartoApiKey; tiles will be watermarked");
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] no secrets.json ({ex.GetType().Name}); tiles will be watermarked");
		}

		return string.Empty;
	}

	// Links in popups and the attribution must not replace the map: they go to the system browser.
	private async void OnNavigating(object? sender, WebNavigatingEventArgs e)
	{
		if (!_ready
			|| !Uri.TryCreate(e.Url, UriKind.Absolute, out Uri? uri)
			|| uri.Scheme is not ("http" or "https"))
		{
			return;
		}

		e.Cancel = true;

		try
		{
			await Launcher.Default.OpenAsync(uri);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Opening a map link failed: {ex.Message}");
		}
	}

	private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
	{
		if (e.Result != WebNavigationResult.Success)
		{
			_loading = false;

			return;
		}

		_ready = true;

		await PushAsync();

		if (_pendingFocus is { } id)
		{
			_pendingFocus = null;

			await FocusAsync(id);
		}
	}

	private async void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
		await PushAsync();

	private async Task PushAsync()
	{
		if (!_ready
			|| _scene is not { } scene)
		{
			return;
		}

		await EvaluateAsync($"ddMap.set({scene.ToJson(IsDark)});");
	}

	private async Task EvaluateAsync(string script)
	{
		try
		{
			await MainThread.InvokeOnMainThreadAsync(
				() => _web.EvaluateJavaScriptAsync(script));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Map script failed: {ex.Message}");
		}
	}
}
