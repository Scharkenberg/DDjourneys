namespace DDjourneys.Core.Models;

/// <summary>A line named by a disruption.</summary>
public sealed class DisruptionLine
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public TransitMode Mode { get; init; }

	public string? Operator { get; init; }
}


public sealed class DisruptionPeriod
{
	public DateTimeOffset? Begin { get; init; }

	public DateTimeOffset? End { get; init; }
}


/// <summary>A route change: construction, detour, closure, disruption.</summary>
public sealed class Disruption
{
	public required string Id { get; init; }

	public required string Title { get; init; }

	/// <summary>Plain text (the provider sends HTML).</summary>
	public string Description { get; init; } = string.Empty;

	/// <summary>The description as the provider sent it (HTML, may contain links, images and PDF links).</summary>
	public string DescriptionHtml { get; init; } = string.Empty;

	/// <summary>Planned (construction) as opposed to a short-term disruption.</summary>
	public bool IsPlanned { get; init; }

	/// <summary>The journey planner takes this change into account.</summary>
	public bool AffectsRouting { get; init; }

	public DateTimeOffset? Published { get; init; }

	public IReadOnlyList<DisruptionLine> Lines { get; init; } = [];

	public IReadOnlyList<DisruptionPeriod> Periods { get; init; } = [];

	/// <summary>True when any period contains <paramref name="moment"/> (or the change names no period).</summary>
	public bool IsActiveAt(DateTimeOffset moment) =>
		Periods.Count == 0
		|| Periods.Any(
			period => (period.Begin is null || period.Begin <= moment)
				&& (period.End is null || period.End >= moment));
}


/// <summary>A general notice of the network (not tied to a line).</summary>
public sealed class NetworkBanner
{
	public required string Title { get; init; }

	public string Description { get; init; } = string.Empty;

	public string DescriptionHtml { get; init; } = string.Empty;

	public DateTimeOffset? Modified { get; init; }
}


public sealed class DisruptionReport
{
	public static DisruptionReport Empty { get; } = new();

	public IReadOnlyList<Disruption> Changes { get; init; } = [];

	public IReadOnlyList<NetworkBanner> Banners { get; init; } = [];
}


public sealed class StopLineDirection
{
	public required string Name { get; init; }

	/// <summary>Names of the timetables (e.g. holiday schedules).</summary>
	public IReadOnlyList<string> Timetables { get; init; } = [];
}


/// <summary>A line that serves a stop.</summary>
public sealed class StopLine
{
	public required string Name { get; init; }

	public TransitMode Mode { get; init; }

	public IReadOnlyList<StopLineDirection> Directions { get; init; } = [];

	/// <summary>Ids of the route changes that affect the line at this stop.</summary>
	public IReadOnlyList<string> RouteChangeIds { get; init; } = [];
}


/// <summary>A stop near a position.</summary>
public sealed class NearbyStop
{
	public required Location Stop { get; init; }

	public int DistanceMeters { get; init; }
}


/// <summary>A tariff zone (fare area) of the network.</summary>
/// <summary>
/// A tariff zone as a shape on the map: number, name, colour, a point inside it (for the label) and its
/// outline in WGS84. Only providers that publish zone outlines fill this (VVO <c>map/polygons</c>).
/// </summary>
public sealed record TariffZoneShape(
	int Number,
	string Name,
	string? Color,
	double CenterLat,
	double CenterLon,
	IReadOnlyList<(double Latitude, double Longitude)> Ring);

public sealed class TariffZone
{
	public required string Number { get; init; }

	public required string Name { get; init; }

	/// <summary>Display colour as "#RRGGBB", when the provider names one.</summary>
	public string? Color { get; init; }

	public override string ToString() =>
		string.IsNullOrWhiteSpace(Name)
			? Number
			: $"{Number} · {Name}";
}


/// <summary>Great-circle distance, for ranking and showing "how far".</summary>
public static class GeoMath
{
	private const double EarthRadiusMeters = 6_371_000;

	public static double DistanceMeters(
		double latitude1,
		double longitude1,
		double latitude2,
		double longitude2)
	{
		double dLat = ToRadians(latitude2 - latitude1);
		double dLon = ToRadians(longitude2 - longitude1);

		double a =
			(Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
			+ (Math.Cos(ToRadians(latitude1))
				* Math.Cos(ToRadians(latitude2))
				* Math.Sin(dLon / 2)
				* Math.Sin(dLon / 2));

		return EarthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
	}

	private static double ToRadians(double degrees) =>
		degrees * Math.PI / 180;
}
