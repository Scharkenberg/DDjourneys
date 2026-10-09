using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Map geometry of a partial route and of a run (GK4 pairs to WGS84).
/// </summary>
public static class VvoPathMapper
{
	public static IReadOnlyList<(double Latitude, double Longitude)> MapPath(
		VvoRoute route,
		VvoPartialRoute partialRoute)
	{
		int index =
			partialRoute.MapDataIndex;

		if (index < 0
			|| index >= route.MapData.Count)
		{
			return [];
		}

		return Parse(route.MapData[index]);
	}

	/// <summary>
	/// The real line of a run (dm/trip asked with map data): every entry of the answer in travel order —
	/// the sliding window of chained journeys as one line, or one line per journey, whichever the service
	/// draws. Empty when the answer carries none.
	/// </summary>
	public static IReadOnlyList<(double Latitude, double Longitude)> MapRunPath(
		VvoRunResponse response)
	{
		ArgumentNullException.ThrowIfNull(response);

		// The wire form may be an absent member or an explicit null; a null that slipped past the
		// converter (the serializer handles nulls itself) must not reach the caller's loop.
		if (response.MapData is not { Count: > 0 })
		{
			return [];
		}

		var path =
			new List<(double Latitude, double Longitude)>();

		foreach (string entry in response.MapData)
		{
			path.AddRange(Parse(entry));
		}

		return path;
	}

	/// <summary>One map data entry ("Tram|5660211|4630082|…"): the mode prefix, then GK4 pairs.</summary>
	private static IReadOnlyList<(double Latitude, double Longitude)> Parse(
		string? mapData)
	{
		if (string.IsNullOrWhiteSpace(mapData))
		{
			return [];
		}

		string[] values =
			mapData.Split(
				'|',
				StringSplitOptions.RemoveEmptyEntries);

		if (values.Length < 3)
		{
			return [];
		}

		var path =
			new List<(double Latitude, double Longitude)>(
				(values.Length - 1) / 2);

		for (int i = 1;
	i + 1 < values.Length;
	i += 2)
		{
			if (!double.TryParse(
				values[i],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out double x))
			{
				continue;
			}

			if (!double.TryParse(
				values[i + 1],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out double y))
			{
				continue;
			}

			path.Add(
				VvoCoordinateConverter.FromGk4(
					y,
					x));
		}

		return path;
	}
}
