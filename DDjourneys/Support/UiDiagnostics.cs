using DDjourneys.Core.Diagnostics;
using System.Text;

namespace DDjourneys.Support;

/// <summary>
/// UI facts for the log (only while "Log to file" is on; nothing is walked otherwise): display, window, theme, density,
/// material, the fonts that were expected and the ones the platform really uses, and what the text and entry controls of
/// a page look like natively. A page is described when it appears and after a theme change, once the layout has settled.
/// Lines are tagged <c>[UI]</c>.
/// </summary>
public static class UiDiagnostics
{
	private static readonly string[] PaletteKeys = ["Bg", "Surface", "Raised", "Outline", "Ink", "InkMuted", "Accent", "OnAccent", "AccentSoft"];

	private static readonly Lock Gate = new();
	private static bool _fontsLogged;
	private static long _lastTicks;
	private static Page? _pending;

	/// <summary>Called when the theme changed: the page on screen is described again.</summary>
	public static void ThemeChanged()
	{
		if (DiagnosticLog.Enabled && Shell.Current?.CurrentPage is { } page)
		{
			Page(page, "theme changed");
		}
	}

	/// <summary>Describes <paramref name="page"/> about a second from now (the layout has settled; bursts count once).</summary>
	public static void Page(Page page, string reason)
	{
		if (!DiagnosticLog.Enabled)
		{
			return;
		}

		lock (Gate)
		{
			if (_pending is not null && Environment.TickCount64 - _lastTicks < 1500)
			{
				return;
			}

			_pending = page;
			_lastTicks = Environment.TickCount64;
		}

		page.Dispatcher.DispatchDelayed(
			TimeSpan.FromMilliseconds(900),
			() =>
			{
				lock (Gate)
				{
					_pending = null;
				}

				try
				{
					Describe(page, reason);
				}
				catch (Exception ex)
				{
					DiagnosticLog.Write($"[UI] description failed: {ex.Message}");
				}
			});
	}

	private static void Describe(Page page, string reason)
	{
		IServiceProvider? services = page.Handler?.MauiContext?.Services;

		DisplayInfo display = DeviceDisplay.Current.MainDisplayInfo;
		Window? window = page.Window;

		DiagnosticLog.Write(
			$"[UI] {reason}: {page.GetType().Name} {page.Width:F0}x{page.Height:F0} dp, window {window?.Width:F0}x{window?.Height:F0}, "
			+ $"display {display.Width:F0}x{display.Height:F0} px @ {display.Density:F2}x {display.Orientation}, "
			+ $"{DeviceInfo.Current.Idiom} {DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}");

		DiagnosticLog.Write(
			$"[UI] theme: mode {Theme.Mode}, colours '{Theme.ColorId}', dark {Theme.IsDark}, pure black {Theme.PureBlack}, "
			+ $"material {Material.Id}/{Material.Surfaces} (active {Material.Active}), density {Density.Profile.Id}, "
			+ $"animations {Motion.Enabled}, system text scale {SystemAccessibility.TextScale:F2}");

		DiagnosticLog.Write("[UI] palette: " + string.Join(", ", PaletteKeys.Select(key => $"{key} {Hex(Theme.ColorOf(key, Colors.Transparent))}")));

		string regular = Resource("FontRegular");
		string semibold = Resource("FontSemibold");

		DiagnosticLog.Write($"[UI] font setting '{Theme.Font}': expected regular '{regular}', semibold '{semibold}'");

		LogFontPlatform(services, regular, semibold);

		var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
		var fonts = new Dictionary<string, (int Count, VisualElement Sample)>(StringComparer.Ordinal);
		var entries = new List<Entry>();

		Walk(page, counts, fonts, entries);

		DiagnosticLog.Write("[UI] elements: " + string.Join(", ", counts.Select(item => $"{item.Key} {item.Value}")));

		foreach ((string key, (int count, VisualElement sample)) in fonts)
		{
			DiagnosticLog.Write($"[UI] text '{key}' x{count}: {Native(sample)}");
		}

		foreach (Entry entry in entries.Take(6))
		{
			DiagnosticLog.Write($"[UI] entry '{Truncate(entry.Placeholder ?? entry.Text)}': {NativeFrame(entry)}");
		}
	}

	private static void Walk(
		IVisualTreeElement element,
		SortedDictionary<string, int> counts,
		Dictionary<string, (int Count, VisualElement Sample)> fonts,
		List<Entry> entries)
	{
		if (element is VisualElement visual)
		{
			string type = visual.GetType().Name;
			counts[type] = counts.GetValueOrDefault(type) + 1;

			if (visual is ITextStyle style && visual is not Entry { } && !string.IsNullOrEmpty(TextOf(visual)))
			{
				string key = $"{(string.IsNullOrEmpty(style.Font.Family) ? "(default)" : style.Font.Family)} / {style.Font.Weight} / {type}";

				fonts[key] = fonts.TryGetValue(key, out var known) ? (known.Count + 1, known.Sample) : (1, visual);
			}
			else if (visual is Entry entry)
			{
				entries.Add(entry);

				string key = $"{(string.IsNullOrEmpty(entry.FontFamily) ? "(default)" : entry.FontFamily)} / Entry";

				fonts[key] = fonts.TryGetValue(key, out var known) ? (known.Count + 1, known.Sample) : (1, visual);
			}
		}

		foreach (IVisualTreeElement child in element.GetVisualChildren())
		{
			Walk(child, counts, fonts, entries);
		}
	}

	private static string TextOf(VisualElement element) =>
		element switch
		{
			Label label => label.Text ?? string.Empty,
			Button button => button.Text ?? string.Empty,
			_ => string.Empty
		};

	private static string Resource(string key) =>
		Application.Current?.Resources.TryGetValue(key, out object? value) == true ? value?.ToString() ?? "(null)" : "(missing)";

	private static string Hex(Color color) => color.ToArgbHex(true);

	private static string Truncate(string? text) =>
		string.IsNullOrEmpty(text) ? string.Empty : text.Length <= 24 ? text : text[..24] + "…";

	private static void LogFontPlatform(IServiceProvider? services, string regular, string semibold)
	{
		lock (Gate)
		{
			if (_fontsLogged)
			{
				return;
			}

			_fontsLogged = true;
		}

		try
		{
			IFontRegistrar? registrar = services?.GetService(typeof(IFontRegistrar)) as IFontRegistrar;

			foreach (string alias in new[] { "InterTightRegular", "InterTightSemiBold", "OpenSansRegular", "OpenSansSemibold", regular, semibold }.Distinct())
			{
				if (string.IsNullOrEmpty(alias))
				{
					continue;
				}

				string? path = null;

				try
				{
					path = registrar?.GetFont(alias);
				}
				catch (Exception ex)
				{
					path = $"error: {ex.Message}";
				}

				DiagnosticLog.Write($"[UI] font alias '{alias}' -> {(path is null ? "NOT REGISTERED / not loadable" : path)}");
			}

#if ANDROID
			global::Android.Content.Res.AssetManager? assets = Microsoft.Maui.ApplicationModel.Platform.AppContext.Assets;

			foreach (string folder in new[] { string.Empty, "fonts" })
			{
				string[] names = assets?.List(folder) ?? [];

				DiagnosticLog.Write($"[UI] assets/{folder}: {(names.Length == 0 ? "(nothing)" : string.Join(", ", names.Where(n => n.Contains('.')).Take(40)))}");
			}
#endif
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[UI] font registry check failed: {ex.Message}");
		}
	}

	/// <summary>What the platform actually uses for this element, and whether that is what the framework resolves for its font.</summary>
	private static string Native(VisualElement element)
	{
		try
		{
			if (element.Handler?.PlatformView is not { } native)
			{
				return "no native view";
			}

			ITextStyle? style = element as ITextStyle;
			IServiceProvider? services = element.Handler.MauiContext?.Services;
			var text = new StringBuilder($"native {native.GetType().Name}");

#if ANDROID
			if (native is global::Android.Widget.TextView view)
			{
				global::Android.Graphics.Typeface? used = view.Typeface;
				global::Android.Graphics.Typeface? expected = style is null ? null : (services?.GetService(typeof(IFontManager)) as IFontManager)?.GetTypeface(style.Font);

				text.Append($", used typeface {Describe(used)}, expected {Describe(expected)}, ")
					.Append(ReferenceEquals(used, expected) || Equals(used, expected) ? "SAME" : "DIFFERENT")
					.Append($", size {view.TextSize:F0} px");
			}

			static string Describe(global::Android.Graphics.Typeface? face) =>
				face is null
					? "null"
					: $"style {face.Style}"
						+ (OperatingSystem.IsAndroidVersionAtLeast(28) ? $", weight {face.Weight}" : string.Empty)
						+ $", #{face.GetHashCode():X}";
#elif WINDOWS
			if (native is Microsoft.UI.Xaml.Controls.TextBlock block)
			{
				text.Append($", used '{block.FontFamily?.Source}' {block.FontWeight.Weight} {block.FontSize:F1}");
			}
			else if (native is Microsoft.UI.Xaml.Controls.Control control)
			{
				text.Append($", used '{control.FontFamily?.Source}' {control.FontWeight.Weight} {control.FontSize:F1}");
			}
#endif

			return text.ToString();
		}
		catch (Exception ex)
		{
			return $"native check failed: {ex.Message}";
		}
	}

	/// <summary>The native frame around an entry: the classes it sits in and the box styling of its text layout.</summary>
	private static string NativeFrame(Entry entry)
	{
		try
		{
			if (entry.Handler?.PlatformView is not { } native)
			{
				return "no native view";
			}

			var text = new StringBuilder($"native {native.GetType().Name}, font: {Native(entry)}");

#if ANDROID
			if (native is global::Android.Views.View view)
			{
				text.Append($"; background {view.Background?.GetType().Name ?? "none"}; parents");

				for (global::Android.Views.IViewParent? parent = view.Parent; parent is not null; parent = parent.Parent)
				{
					text.Append(" > ").Append(parent.GetType().Name);

					if (parent is Google.Android.Material.TextField.TextInputLayout layout)
					{
						text.Append($"[box mode {layout.BoxBackgroundMode}, stroke {layout.BoxStrokeWidth}/{layout.BoxStrokeWidthFocused}, background {layout.Background?.GetType().Name ?? "none"}]");
					}

					if (parent is not global::Android.Views.View)
					{
						break;
					}
				}

				if (view is global::Android.Views.ViewGroup group)
				{
					for (int i = 0; i < group.ChildCount; i++)
					{
						if (group.GetChildAt(i) is Google.Android.Material.TextField.TextInputLayout inner)
						{
							text.Append($"; child {inner.GetType().Name}[box mode {inner.BoxBackgroundMode}]");
						}
					}
				}
			}
#endif

			return text.ToString();
		}
		catch (Exception ex)
		{
			return $"native check failed: {ex.Message}";
		}
	}
}
