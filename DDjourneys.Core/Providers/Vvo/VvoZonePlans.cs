using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DDjourneys.Core.Providers.Vvo;

/// <summary>The two interactive plans of the VVO website ("Tarifzonenplan interaktiv").</summary>
public enum VvoZonePlanKind
{
	/// <summary>The fare zone plan of the whole VVO area with the line network overview.</summary>
	Region,

	/// <summary>The DVB line network plan of Dresden.</summary>
	Dresden
}

/// <summary>A stop on a plan: VVO stop id, place (null in Dresden), name, and its position in plan pixels (deepest level).</summary>
public sealed record VvoPlanPin(string Id, string? Place, string Name, double X, double Y)
{
	/// <summary>"Name, Place" like the VVO page (the place is left out in Dresden).</summary>
	public string Title =>
		string.IsNullOrWhiteSpace(Place) ? Name : $"{Name}, {Place}";
}

/// <summary>
/// What the VVO website's interactive plans are made of (vvo-online.de, "Tarifzonen im Detail"; read from its
/// script bundle and checked against the tiles, October 2026): pre-cut 256 px tiles on maps.vvo-online.de in a
/// pyramid whose levels are NOT powers of two, and a pin list whose coordinates are pixels of the deepest level
/// (the parameter <c>coordinatesystem=epsg3857</c> notwithstanding). The level scales were measured by matching
/// each level against the deepest one; the page's own tile counts are rough (some levels serve stale tiles beyond
/// the plan, the region's shallowest level has a "please zoom in" notice baked in, so it is not used).
/// </summary>
public static class VvoZonePlans
{
	public const string TileHost = "https://maps.vvo-online.de/netzplan/";

	public const string PinsUrl = "https://www.vvo-online.de/map/pins?coordinatesystem=epsg3857&type=";

	/// <summary>The geometry of a plan in pixels of its deepest level.</summary>
	public static (double Width, double Height, IReadOnlyList<double> Scales, int MinLevel) Geometry(VvoZonePlanKind kind) =>
		kind == VvoZonePlanKind.Region
			? (7202, 5100, [0.2335, 0.4055, 0.6495, 1], 1)
			: (5888, 4352, [0.2405, 0.6695, 1], 0);

	/// <summary>The address of a tile (level, column, row).</summary>
	public static string TileUrl(VvoZonePlanKind kind, int level, int x, int y) =>
		kind == VvoZonePlanKind.Region
			? string.Create(CultureInfo.InvariantCulture, $"{TileHost}vvo/Level{level}/{y}-{x}.png")
			: string.Create(CultureInfo.InvariantCulture, $"{TileHost}dresden/Level{level}/Tile_{y:000}-{x:000}.png");

	/// <summary>The address of the pin list.</summary>
	public static string PinsAddress(VvoZonePlanKind kind) =>
		PinsUrl + (kind == VvoZonePlanKind.Region ? "route" : "dresden");

	/// <summary>
	/// Tiles whose content names the plan's edition (title, "Stand ..." / "gültig ab ..."): with the pins they make
	/// the fingerprint of a version, so a new plan never mixes with tiles of the old one.
	/// </summary>
	public static IReadOnlyList<(int Level, int X, int Y)> EditionTiles(VvoZonePlanKind kind) =>
		kind == VvoZonePlanKind.Region
			? [(1, 0, 0), (1, 0, 1), (1, 1, 0)]
			: [(1, 13, 0), (1, 13, 1), (1, 14, 0)];

	/// <summary>
	/// Reads the pin list: a JSON array of "id||place|name|y|x||" strings (an empty place means Dresden). Broken
	/// entries are skipped; a broken document gives an empty list.
	/// </summary>
	public static IReadOnlyList<VvoPlanPin> ParsePins(ReadOnlySpan<byte> json)
	{
		var pins = new List<VvoPlanPin>();

		try
		{
			var reader = new Utf8JsonReader(json);

			if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
			{
				return pins;
			}

			while (reader.Read() && reader.TokenType == JsonTokenType.String)
			{
				if (Pin(reader.GetString()) is { } pin)
				{
					pins.Add(pin);
				}
			}
		}
		catch (JsonException)
		{
		}

		return pins;
	}

	private static VvoPlanPin? Pin(string? entry)
	{
		string[] parts = (entry ?? string.Empty).Split('|');

		return parts.Length >= 6
			&& !string.IsNullOrWhiteSpace(parts[0])
			&& !string.IsNullOrWhiteSpace(parts[3])
			&& double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
			&& double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
				? new VvoPlanPin(parts[0].Trim(), string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2].Trim(), parts[3].Trim(), x, y)
				: null;
	}

	/// <summary>
	/// The VVO stop id for a stop of any provider: VVO ids as they are, DHIDs of the area ("de:14612:28", platform
	/// parts allowed) as "33" + the stop number in six digits ("33000028") - the scheme the VVO ids follow.
	/// </summary>
	public static string? VvoIdOf(string? stopId)
	{
		if (string.IsNullOrWhiteSpace(stopId))
		{
			return null;
		}

		string id = stopId.Trim();

		if (id.Length == 8 && id.StartsWith("33", StringComparison.Ordinal) && id.All(char.IsAsciiDigit))
		{
			return id;
		}

		string[] parts = id.Split(':');

		return parts.Length >= 3
			&& parts[0].Equals("de", StringComparison.OrdinalIgnoreCase)
			&& parts[2].Length is > 0 and <= 6
			&& parts[2].All(char.IsAsciiDigit)
				? "33" + parts[2].PadLeft(6, '0')
				: null;
	}

	/// <summary>
	/// A stop name reduced for comparison: lower case, umlauts and accents folded, "straße"/"strasse" as "str",
	/// nothing but letters and digits ("Dr.-Külz-Ring" and "Dr Kuelz Ring" meet).
	/// </summary>
	public static string Fold(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		string lower = text.ToLowerInvariant()
			.Replace("ä", "ae", StringComparison.Ordinal)
			.Replace("ö", "oe", StringComparison.Ordinal)
			.Replace("ü", "ue", StringComparison.Ordinal)
			.Replace("ß", "ss", StringComparison.Ordinal)
			.Replace("strasse", "str", StringComparison.Ordinal)
			.Normalize(NormalizationForm.FormD);

		var folded = new StringBuilder(lower.Length);

		foreach (char c in lower)
		{
			if (char.IsAsciiLetterOrDigit(c))
			{
				folded.Append(c);
			}
		}

		return folded.ToString();
	}
}
