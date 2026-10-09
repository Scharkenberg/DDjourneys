using System.Globalization;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Converts the Gauss-Krueger zone 4 coordinates (DHDN, EPSG:31468) of the VVO WebAPI to WGS84.
/// One place for every VVO coordinate: trip stops, map paths and PointFinder results.
/// </summary>
public static class VvoCoordinateConverter
{
	// GK4 easting is the zone's false easting (4 500 000) plus/minus the distance from the 12th meridian
	// (about 4.2 to 4.8 million in Saxony); the northing is the distance from the equator (about 5.5 to
	// 5.7 million in Saxony). The ranges are generous and exist to reject "0", empty and swapped values.
	private const double MinEasting = 3_000_000;
	private const double MaxEasting = 5_000_000;
	private const double MinNorthing = 5_000_000;
	private const double MaxNorthing = 7_000_000;

	private static readonly Lazy<ICoordinateTransformation> Gk4ToWgs84 =
		new(CreateGk4ToWgs84Transformation);

	// The inverse direction is asked for per coordinate query: built once, like the forward one.
	private static readonly Lazy<MathTransform> Wgs84ToGk4 =
		new(() => Gk4ToWgs84.Value.MathTransform.Inverse());

	/// <summary>Converts a GK4 point; <paramref name="easting"/> is the "Rechtswert", <paramref name="northing"/> the "Hochwert".</summary>
	public static (double Latitude, double Longitude) FromGk4(
		double easting,
		double northing)
	{
		double[] result =
			Gk4ToWgs84.Value.MathTransform.Transform(
				[easting, northing]);

		return (
			Latitude: result[1],
			Longitude: result[0]);
	}

	/// <summary>
	/// Converts a WGS84 point to GK4 (the form the PointFinder coordinate query takes).
	/// </summary>
	/// <returns>False when the point is not finite or lies outside zone 4 (the result would be meaningless).</returns>
	public static bool TryToGk4(
		double latitude,
		double longitude,
		out (double Easting, double Northing) result)
	{
		result = default;

		if (!double.IsFinite(latitude)
			|| !double.IsFinite(longitude)
			|| latitude is < 46 or > 56
			|| longitude is < 9 or > 15)
		{
			return false;
		}

		double[] gk4 =
			Wgs84ToGk4.Value.Transform(
				[longitude, latitude]);

		if (gk4[0] is < MinEasting or >= MaxEasting
			|| gk4[1] is < MinNorthing or >= MaxNorthing)
		{
			return false;
		}

		result = (gk4[0], gk4[1]);

		return true;
	}

	/// <summary>
	/// Converts the two raw coordinate fields of a VVO PointFinder entry. The API names the first field
	/// like a latitude (northing) and the second like a longitude (easting); the values are told apart by
	/// magnitude anyway, so a swapped pair does not produce a point in the wrong place.
	/// </summary>
	/// <returns>False when the fields are empty, "0" or not a plausible GK4 point.</returns>
	public static bool TryFromPointFields(
		string? first,
		string? second,
		out (double Latitude, double Longitude) result)
	{
		result = default;

		if (!TryParse(first, out double a)
			|| !TryParse(second, out double b))
		{
			return false;
		}

		(double northing, double easting) =
			a >= MinNorthing
				? (a, b)
				: (b, a);

		if (northing is < MinNorthing or >= MaxNorthing
			|| easting is < MinEasting or >= MaxEasting)
		{
			return false;
		}

		result =
			FromGk4(
				easting,
				northing);

		return true;
	}

	private static bool TryParse(
		string? text,
		out double value) =>
		double.TryParse(
			text,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out value)
		&& double.IsFinite(value);

	private static ICoordinateTransformation CreateGk4ToWgs84Transformation()
	{
		CoordinateSystemFactory coordinateSystemFactory =
			new();

		CoordinateTransformationFactory transformationFactory =
			new();

		const string gk4Wkt =
			"""
			PROJCS["DHDN / 3-degree Gauss-Kruger zone 4",
				GEOGCS["DHDN",
					DATUM["Deutsches_Hauptdreiecksnetz",
						SPHEROID["Bessel 1841",6377397.155,299.1528128],
						TOWGS84[598.1,73.7,418.2,0.202,0.045,-2.455,6.7]],
					PRIMEM["Greenwich",0],
					UNIT["degree",0.0174532925199433]],
				PROJECTION["Transverse_Mercator"],
				PARAMETER["latitude_of_origin",0],
				PARAMETER["central_meridian",12],
				PARAMETER["scale_factor",1],
				PARAMETER["false_easting",4500000],
				PARAMETER["false_northing",0],
				UNIT["metre",1]]
			""";

		CoordinateSystem source =
			coordinateSystemFactory.CreateFromWkt(
				gk4Wkt);

		CoordinateSystem target =
			GeographicCoordinateSystem.WGS84;

		return transformationFactory
			.CreateFromCoordinateSystems(
				source,
				target);
	}
}
