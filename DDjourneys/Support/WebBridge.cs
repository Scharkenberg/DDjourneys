using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Mapping;

namespace DDjourneys.Support;

/// <summary>
/// What the app's two web pages (the map and the notice viewer, both in a <see cref="HybridWebView"/>) share:
/// calling a page function with string arguments and the active theme as the pages need it.
/// </summary>
public static class WebBridge
{
	// Built by hand: a [JsonSerializable] context needs the System.Text.Json source generator, which does not
	// run in this solution (same as [GeneratedRegex]). This is the AOT-safe metadata for a plain string.
	private static readonly JsonTypeInfo<string> StringInfo =
		JsonMetadataServices.CreateValueInfo<string>(
			new JsonSerializerOptions { TypeInfoResolver = JsonTypeInfoResolver.Combine() },
			JsonMetadataServices.StringConverter);

	/// <summary>Calls <c>function(command, json)</c> in the page; failures are logged, never thrown.</summary>
	public static async Task CallAsync(
		HybridWebView web,
		string function,
		string command,
		string? json,
		string log)
	{
		ArgumentNullException.ThrowIfNull(web);

		try
		{
			await MainThread.InvokeOnMainThreadAsync(
				() => web.InvokeJavaScriptAsync<string>(
					function,
					null,
					[command, json],
					[StringInfo, StringInfo]));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[{log}] '{command}' failed: {ex.Message}");
		}
	}

	/// <summary>The colours of the active app theme, in the form the pages take.</summary>
	public static MapTheme CurrentTheme()
	{
		static string Hex(string key, string fallback)
		{
			Color color = Theme.ColorOf(key, Color.FromArgb(fallback));

			return $"#{(int)Math.Round(color.Red * 255):X2}{(int)Math.Round(color.Green * 255):X2}{(int)Math.Round(color.Blue * 255):X2}";
		}

		return
			Theme.IsDark
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
}
