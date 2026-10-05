using System.Text;
using System.Text.Json;

namespace DDjourneys.Core.Mapping;

/// <summary>
/// The colours of the app theme in effect, as the map page needs them to recolour the basemap
/// (hex strings, "#RRGGBB"). <see cref="Ok"/> is the theme's "on time" colour, used for green areas.
/// <see cref="Font"/> is the app's font face id (the page ships the same fonts) and <see cref="FontScale"/>
/// the OS text size factor, so the map text follows the app's typography and accessibility settings.
/// </summary>
public sealed record MapTheme(
	bool Dark,
	string Bg,
	string Surface,
	string Raised,
	string Outline,
	string Ink,
	string InkMuted,
	string Accent,
	string AccentSoft,
	string Ok,
	string Font = "opensans",
	double FontScale = 1)
{
	/// <summary>The JSON for <c>ddMapCall('theme', ...)</c>.</summary>
	public string ToJson()
	{
		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream))
		{
			Write(writer);
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	/// <summary>
	/// The JSON for <c>ddMapCall('init', ...)</c>: the CARTO key (may be empty), the theme, whether the view
	/// follows the scene, and the texts of the map's own controls.
	/// </summary>
	public string ToInitJson(
		string cartoKey,
		bool autoFit,
		string autoFitLabel,
		string fitNowLabel,
		string infoLabel)
	{
		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("key", cartoKey);
			writer.WriteBoolean("autoFit", autoFit);
			writer.WritePropertyName("labels");
			writer.WriteStartObject();
			writer.WriteString("autoFit", autoFitLabel);
			writer.WriteString("fitNow", fitNowLabel);
			writer.WriteString("info", infoLabel);
			writer.WriteEndObject();
			writer.WritePropertyName("theme");
			Write(writer);
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private void Write(Utf8JsonWriter writer)
	{
		writer.WriteStartObject();
		writer.WriteBoolean("dark", Dark);
		writer.WriteString("bg", Bg);
		writer.WriteString("surface", Surface);
		writer.WriteString("raised", Raised);
		writer.WriteString("outline", Outline);
		writer.WriteString("ink", Ink);
		writer.WriteString("inkMuted", InkMuted);
		writer.WriteString("accent", Accent);
		writer.WriteString("accentSoft", AccentSoft);
		writer.WriteString("ok", Ok);
		writer.WriteString("font", Font);
		writer.WriteNumber("fontScale", FontScale);
		writer.WriteEndObject();
	}
}
