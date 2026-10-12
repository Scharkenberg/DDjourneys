namespace DDjourneys.Core.Mapping;

/// <summary>
/// Which Web Mercator tiles make up the offline city map of Dresden: the bounding box (the city and its close
/// outskirts, the map's own default centre in the middle of it), the tile of a position, the enumeration of the
/// set (shallowest zoom first), the honest size estimate (the count times an average tile weight, never a guess
/// of the whole set) and whether a tile the map asks for belongs to the set. Pure math, unit-tested; the
/// download and the file cache live in the app (<c>CityTilesStore</c>), the serving on the map bridge.
/// </summary>
public static class CityTiles
{
	/// <summary>Southern edge of the box: the city and its close outskirts reach down to about 51.00°.</summary>
	public const double SouthLatitude = 51.00;

	/// <summary>Northern edge of the box (Dresden plus the communities that touch it).</summary>
	public const double NorthLatitude = 51.11;

	/// <summary>Western edge of the box.</summary>
	public const double WestLongitude = 13.63;

	/// <summary>Eastern edge of the box. The box is fixed: it is the city map, not a follower of the viewport.</summary>
	public const double EastLongitude = 13.85;

	/// <summary>Shallowest level kept: the whole city on two tiles, the first thing the map needs.</summary>
	public const int MinZoom = 10;

	/// <summary>
	/// Deepest level kept. 15 is street-detail sharp on a 256 px tile; every level deeper roughly quadruples the
	/// count (z16 alone would add about 1300 tiles of the city), and the Leaflet engine's own cap of 19 exists for
	/// reading a map, not for storing one — deeper levels are fetched live whenever there is a connection, and the
	/// prefetch is a one-time comfort download, polite to the tile hosts.
	/// </summary>
	public const int MaxZoom = 15;

	/// <summary>
	/// The honest average weight of one 256 px raster tile of OSM/CARTO; the estimate shown before a download is
	/// the count times this. An average, said as one: the real size is measured after the download.
	/// </summary>
	public const long BytesPerTile = 20 * 1024;

	private const double WebMercatorLimit = 85.05112878;

	/// <summary>The tile of a position at a zoom level (the standard slippy-map math; latitudes beyond the
	/// projection's limits are clamped onto its edge).</summary>
	public static (int X, int Y) TileOf(double latitude, double longitude, int zoom)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(zoom);

		double n = 1L << zoom;
		double x = (longitude + 180.0) / 360.0 * n;
		double lat = Math.Clamp(latitude, -WebMercatorLimit, WebMercatorLimit);
		double radians = lat * Math.PI / 180.0;
		double y = (1.0 - Math.Log(Math.Tan(radians) + 1.0 / Math.Cos(radians)) / Math.PI) / 2.0 * n;

		return ((int)Math.Floor(x), (int)Math.Floor(y));
	}

	/// <summary>Every tile of the set, shallowest zoom first, rows north to south (the overview arrives before the detail).</summary>
	public static IEnumerable<(int Zoom, int X, int Y)> Tiles(
		int minZoom = MinZoom,
		int maxZoom = MaxZoom)
	{
		foreach ((int zoom, int firstX, int lastX, int firstY, int lastY) in Levels(minZoom, maxZoom))
		{
			for (int y = firstY; y <= lastY; y++)
			{
				for (int x = firstX; x <= lastX; x++)
				{
					yield return (zoom, x, y);
				}
			}
		}
	}

	/// <summary>How many tiles the set holds.</summary>
	public static int Count(int minZoom = MinZoom, int maxZoom = MaxZoom)
	{
		int count = 0;

		foreach ((int _, int firstX, int lastX, int firstY, int lastY) in Levels(minZoom, maxZoom))
		{
			count += (lastX - firstX + 1) * (lastY - firstY + 1);
		}

		return count;
	}

	/// <summary>The honest estimate for the whole set: the count times the average tile weight.</summary>
	public static long EstimateBytes(int minZoom = MinZoom, int maxZoom = MaxZoom) =>
		(long)Count(minZoom, maxZoom) * BytesPerTile;

	/// <summary>
	/// Whether a tile the map asks for belongs to the set: offline serving answers only these, so a position
	/// outside the city or a level deeper than <see cref="MaxZoom"/> gets the map's own miss, never a stretched
	/// or stale picture.
	/// </summary>
	public static bool Within(int zoom, int x, int y)
	{
		if (zoom is < MinZoom or > MaxZoom)
		{
			return false;
		}

		(int _, int firstX, int lastX, int firstY, int lastY) = Level(zoom);

		return x >= firstX && x <= lastX && y >= firstY && y <= lastY;
	}

	private static IEnumerable<(int Zoom, int FirstX, int LastX, int FirstY, int LastY)> Levels(
		int minZoom,
		int maxZoom)
	{
		int first = Math.Max(MinZoom, Math.Min(minZoom, maxZoom));
		int last = Math.Min(MaxZoom, Math.Max(minZoom, maxZoom));

		for (int zoom = first; zoom <= last; zoom++)
		{
			yield return Level(zoom);
		}
	}

	private static (int Zoom, int FirstX, int LastX, int FirstY, int LastY) Level(int zoom)
	{
		// The north-west corner names the first column and row, the south-east corner the last ones; the box
		// never crosses the antimeridian (it is Dresden), so no wrap-around is handled.
		(int westX, int firstY) = TileOf(NorthLatitude, WestLongitude, zoom);
		(int eastX, int lastY) = TileOf(SouthLatitude, EastLongitude, zoom);

		return (zoom, Math.Min(westX, eastX), Math.Max(westX, eastX), Math.Min(firstY, lastY), Math.Max(firstY, lastY));
	}
}
