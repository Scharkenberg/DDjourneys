using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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

	// Built by hand: a [JsonSerializable] context needs the System.Text.Json source generator, which does not
	// run in this solution (same as [GeneratedRegex]). This is the AOT-safe metadata for a plain string.
	private static readonly JsonTypeInfo<string> StringInfo =
		JsonMetadataServices.CreateValueInfo<string>(
			new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine() },
			JsonMetadataServices.StringConverter);

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

		Content = _web;

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

		await CallAsync("init", CurrentTheme().ToInitJson(key));

		_ready = true;

		await PushSceneAsync();

		if (_pendingFocus is { } id)
		{
			_pendingFocus = null;

			await FocusAsync(id);
		}
	}

	private static MapTheme CurrentTheme()
	{
		static string Hex(string key, string fallback)
		{
			Color color = Theme.ColorOf(key, Color.FromArgb(fallback));

			return $"#{(int)Math.Round(color.Red * 255):X2}{(int)Math.Round(color.Green * 255):X2}{(int)Math.Round(color.Blue * 255):X2}";
		}

		bool dark = Theme.IsDark;

		return
			dark
				? new MapTheme(
					true,
					Hex("Bg", "#0C1418"), Hex("Surface", "#16232A"), Hex("Raised", "#23343E"), Hex("Outline", "#456070"),
					Hex("Ink", "#E9EFF1"), Hex("InkMuted", "#A2B3BC"), Hex("Accent", "#5CC0DA"), Hex("AccentSoft", "#1B3E4A"),
					Hex("OnTime", "#4ADE80"))
				: new MapTheme(
					false,
					Hex("Bg", "#F2F4F5"), Hex("Surface", "#FFFFFF"), Hex("Raised", "#DFE6E9"), Hex("Outline", "#B4C1C7"),
					Hex("Ink", "#0F1A1F"), Hex("InkMuted", "#4A5960"), Hex("Accent", "#0B6E8A"), Hex("AccentSoft", "#D3E9F0"),
					Hex("OnTime", "#15803D"));
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

		await CallAsync("theme", CurrentTheme().ToJson());
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

	private async Task CallAsync(string command, string? json)
	{
		try
		{
			await MainThread.InvokeOnMainThreadAsync(
				() => _web.InvokeJavaScriptAsync<string>(
					"ddMapCall",
					null,
					[command, json],
					[StringInfo, StringInfo]));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] '{command}' failed: {ex.Message}");
		}
	}
}
