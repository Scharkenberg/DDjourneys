using System.Text;
using System.Text.Json;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Controls;

/// <summary>
/// Shows the HTML of a notice (a disruption, a network message) in a <see cref="HybridWebView"/>. The page
/// (Resources/Raw/wwwroot/notice.html) rebuilds the markup through a whitelist, so only text, lists, tables,
/// links and pictures remain, in the colours of the app theme. A tap on a link is not followed: it is reported
/// through <see cref="LinkTapped"/> so the app decides what to do with it (see <see cref="NoticeLinks"/>); a tap
/// on a picture opens the page's own image viewer.
/// </summary>
public sealed partial class NoticeView : ContentView
{
	public static readonly BindableProperty HtmlProperty =
		BindableProperty.Create(
			nameof(Html),
			typeof(string),
			typeof(NoticeView),
			string.Empty,
			propertyChanged: (view, _, _) => _ = ((NoticeView)view).PushHtmlAsync());

	private readonly HybridWebView _web;
	private bool _ready;
	private bool _subscribed;

	public NoticeView()
	{
		_web =
			new HybridWebView
			{
				DefaultFile = "notice.html",
				HorizontalOptions = LayoutOptions.Fill,
				VerticalOptions = LayoutOptions.Fill
			};

		_web.RawMessageReceived += OnRawMessage;

		Content = _web;

		HandlerChanged += OnHandlerChanged;
	}

	/// <summary>The notice as HTML (or plain text wrapped in paragraphs).</summary>
	public string Html
	{
		get => (string)GetValue(HtmlProperty);
		set => SetValue(HtmlProperty, value);
	}

	/// <summary>A link of the notice was tapped (absolute address; http, https, mailto or tel).</summary>
	public event EventHandler<Uri>? LinkTapped;

	/// <summary>Shows a picture in the page's viewer (zoomable, closes with the button).</summary>
	public Task ShowImageAsync(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);

		return CallAsync("image", Write(writer => writer.WriteString("url", uri.AbsoluteUri)));
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

	private async void OnThemeChanged(object? sender, EventArgs e)
	{
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

				await PushInitAsync();
			}
			else if (message.StartsWith("link:", StringComparison.Ordinal)
				&& Uri.TryCreate(message[5..], UriKind.Absolute, out Uri? uri)
				&& uri.Scheme is "http" or "https" or "mailto" or "tel")
			{
				LinkTapped?.Invoke(this, uri);
			}
			else if (message.StartsWith("error:", StringComparison.Ordinal))
			{
				DiagnosticLog.Write($"[Notice JS] {message[6..]}");
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Notice] message '{message}' failed: {ex.Message}");
		}
	}

	private Task PushInitAsync()
	{
		ExtrasStrings strings = LocalizationService.Current.CurrentStrings.Extras;

		string json =
			Write(
				writer =>
				{
					writer.WritePropertyName("theme");
					writer.WriteRawValue(WebBridge.CurrentTheme().ToJson());
					writer.WriteString("html", Html);
					writer.WritePropertyName("labels");
					writer.WriteStartObject();
					writer.WriteString("close", strings.NoticeClose);
					writer.WriteString("imageFailed", strings.NoticeImageFailed);
					writer.WriteString("empty", strings.NoticeEmpty);
					writer.WriteEndObject();
				});

		return CallAsync("init", json);
	}

	private Task PushHtmlAsync() =>
		_ready
			? CallAsync("html", Write(writer => writer.WriteString("html", Html)))
			: Task.CompletedTask;

	private static string Write(Action<Utf8JsonWriter> body)
	{
		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			body(writer);
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private Task CallAsync(string command, string json) =>
		WebBridge.CallAsync(_web, "ddNoticeCall", command, json, "Notice");
}
