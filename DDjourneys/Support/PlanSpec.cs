using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DDjourneys.Support;

/// <summary>
/// The geometry of a plan shown by <see cref="Controls.PlanView"/>: a picture of any size, cut into a pyramid of
/// tiles (or one picture, <see cref="Image"/>). One unit is one CSS pixel at the deepest native level.
/// </summary>
/// <param name="Key">Names this content in the tile addresses (<c>/_plan/{key}/...</c>); a new version needs a new key.</param>
/// <param name="Width">Width in units.</param>
/// <param name="Height">Height in units.</param>
/// <param name="Scales">CSS pixels per unit at each native level, ascending; the last is 1. Need not be powers of two.</param>
/// <param name="TileSize">Edge of a tile in CSS pixels.</param>
/// <param name="MinNativeLevel">The shallowest level the source serves (smaller views scale it down).</param>
/// <param name="OverZoom">Zoom steps beyond the deepest level (its tiles scaled up); fractions allowed.</param>
/// <param name="Retina">Tiles are made for the screen's pixel density (vector sources); the ratio comes with each request.</param>
/// <param name="Image">One picture instead of tiles (at full size, never scaled down).</param>
/// <param name="Paper">The sheet under the tiles, "#RRGGBB".</param>
public sealed record PlanSpec(
	string Key,
	double Width,
	double Height,
	IReadOnlyList<double> Scales,
	int TileSize = 256,
	int MinNativeLevel = 0,
	double OverZoom = 1,
	bool Retina = false,
	bool Image = false,
	string Paper = "#FFFFFF")
{
	/// <summary>Width and height of a native level in CSS pixels.</summary>
	public (double Width, double Height) LevelSize(int level) =>
		(Width * Scales[level], Height * Scales[level]);

	/// <summary>The JSON the plan page takes (<c>ddPlanCall('init', ...)</c>, property <c>plan</c>).</summary>
	public void Write(Utf8JsonWriter writer)
	{
		ArgumentNullException.ThrowIfNull(writer);

		writer.WriteStartObject();
		writer.WriteString("key", Key);
		writer.WriteNumber("width", Width);
		writer.WriteNumber("height", Height);
		writer.WriteStartArray("scales");

		foreach (double scale in Scales)
		{
			writer.WriteNumberValue(scale);
		}

		writer.WriteEndArray();
		writer.WriteNumber("tileSize", TileSize);
		writer.WriteNumber("minNative", MinNativeLevel);
		writer.WriteNumber("overZoom", OverZoom);
		writer.WriteBoolean("retina", Retina);
		writer.WriteBoolean("image", Image);
		writer.WriteString("paper", Paper);
		writer.WriteEndObject();
	}
}

/// <summary>One tile of a plan: native level, column, row and the pixel ratio it is wanted for (1 to 3).</summary>
public readonly record struct PlanTile(int Level, int X, int Y, int Ratio);

/// <summary>
/// Where the pictures of a plan come from. Called on web view threads, any number at once: implementations are
/// thread-safe and never need the UI thread. Owned by the <see cref="Controls.PlanView"/> that shows them.
/// </summary>
public interface IPlanTiles : IDisposable
{
	PlanSpec Spec { get; }

	/// <summary>The media type of what <see cref="OpenTileAsync"/> and <see cref="OpenImageAsync"/> deliver.</summary>
	string ContentType { get; }

	/// <summary>The tile, or null when there is none (outside the plan, or it could not be made).</summary>
	Task<Stream?> OpenTileAsync(PlanTile tile, CancellationToken cancellationToken);

	/// <summary>The whole picture of an <see cref="PlanSpec.Image"/> plan; null for a tiled one.</summary>
	Task<Stream?> OpenImageAsync(CancellationToken cancellationToken);
}

/// <summary>The addresses the plan page asks for: <c>/_plan/{key}/{level}/{x}/{y}?r={ratio}</c> and <c>/_plan/{key}/image</c>.</summary>
public static class PlanAddress
{
	public const string Prefix = "/_plan/";

	/// <summary>Reads a request of the page; false for anything that is not this plan's.</summary>
	public static bool TryRead(Uri uri, string key, out PlanTile tile, out bool image)
	{
		ArgumentNullException.ThrowIfNull(uri);

		tile = default;
		image = false;

		string path = uri.AbsolutePath;

		if (!path.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return false;
		}

		string[] parts = path[Prefix.Length..].Split('/');

		if (parts.Length < 2
			|| !string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.Ordinal))
		{
			return false;
		}

		if (parts.Length == 2)
		{
			image = parts[1] == "image";

			return image;
		}

		if (parts.Length != 4
			|| !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
			|| !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
			|| !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
		{
			return false;
		}

		tile = new PlanTile(level, x, y, Ratio(uri.Query));

		return true;
	}

	private static int Ratio(string query)
	{
		const string Name = "r=";

		int at = query.IndexOf(Name, StringComparison.Ordinal);

		if (at < 0)
		{
			return 1;
		}

		int end = query.IndexOf('&', at);
		string value = end < 0 ? query[(at + Name.Length)..] : query[(at + Name.Length)..end];

		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ratio)
			? Math.Clamp(ratio, 1, 3)
			: 1;
	}

	/// <summary>A short, address-safe fingerprint of a file's content (names a plan version).</summary>
	public static string Fingerprint(string path)
	{
		using FileStream stream = File.OpenRead(path);

		byte[] hash = System.Security.Cryptography.SHA256.HashData(stream);

		return Convert.ToHexStringLower(hash.AsSpan(0, 8));
	}

	internal static string Json(Action<Utf8JsonWriter> body)
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
}
