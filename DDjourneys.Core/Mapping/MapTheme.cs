using System.Text;
using System.Text.Json;

namespace DDjourneys.Core.Mapping;

/// <summary>
/// The colours of the app theme in effect, as the map page needs them to recolour the basemap
/// (hex strings, "#RRGGBB"). <see cref="Ok"/> is the theme's "on time" colour, used for green areas.
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
	string Ok)
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

	/// <summary>The JSON for <c>ddMapCall('init', ...)</c>: the CARTO key (may be empty) and the theme.</summary>
	public string ToInitJson(string cartoKey)
	{
		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteString("key", cartoKey);
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
		writer.WriteEndObject();
	}
}
