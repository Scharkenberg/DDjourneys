using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Map geometry of a partial route (GK4 pairs to WGS84).
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

		string? mapData =
			route.MapData[index];

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
