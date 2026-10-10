namespace DDjourneys.Core.Theming;

/// <summary>
/// Sunrise and sunset from the NOAA solar equations, computed locally: no network, no location
/// permission, the caller names the place. All times are UTC instants. Polar day and polar night
/// have no events; the declination of the day tells light from dark there.
/// </summary>
public static class SunTimes
{
	/// <summary>Official sunrise and sunset use this zenith: the edge of the sun at the horizon, refraction included.</summary>
	private const double ZenithDegrees = 90.833;
	private const double Degrees = Math.PI / 180;

	/// <summary>Sunrise and sunset on that calendar day, in UTC; null when the day has no such event.</summary>
	public static (DateTimeOffset? Sunrise, DateTimeOffset? Sunset) For(double latitude, double longitude, DateOnly date)
	{
		double declination = Declination(date);
		double latRad = latitude * Degrees;

		double cosHa =
			Math.Cos(ZenithDegrees * Degrees) / (Math.Cos(latRad) * Math.Cos(declination))
			- Math.Tan(latRad) * Math.Tan(declination);

		if (cosHa is < -1 or > 1)
		{
			return (null, null);
		}

		double ha = Math.Acos(cosHa) / Degrees;
		double eqTime = EquationOfTime(date);

		return (
			Instant(date, 720 - 4 * (longitude + ha) - eqTime),
			Instant(date, 720 - 4 * (longitude - ha) - eqTime));
	}

	/// <summary>The earlier of the next sunrise and sunset after <paramref name="after"/>, in UTC; null in the polar belt.</summary>
	public static DateTimeOffset? NextBoundary(double latitude, double longitude, DateTimeOffset after)
	{
		DateTimeOffset utc = after.ToUniversalTime();
		DateOnly day = DateOnly.FromDateTime(utc.UtcDateTime);

		for (int i = 0; i < 3; i++)
		{
			(DateTimeOffset? sunrise, DateTimeOffset? sunset) = For(latitude, longitude, day.AddDays(i));

			DateTimeOffset? next =
				[sunrise, sunset]
					.OfType<DateTimeOffset>()
					.Where(time => time > utc)
					.OrderBy(time => time)
					.FirstOrDefault();

			if (next is { } boundary)
			{
				return boundary;
			}
		}

		return null;
	}

	/// <summary>
	/// True while the day is lit: from the given margin before sunrise until the same margin after sunset
	/// (the sky is lit first and last). Polar days are lit, polar nights are not (the declination decides).
	/// </summary>
	public static bool IsDaylight(double latitude, double longitude, DateTimeOffset at, TimeSpan margin)
	{
		DateTimeOffset utc = at.ToUniversalTime();
		(DateTimeOffset? sunrise, DateTimeOffset? sunset) = For(latitude, longitude, DateOnly.FromDateTime(utc.UtcDateTime));

		if (sunrise is { } up && sunset is { } down)
		{
			return utc >= up - margin && utc <= down + margin;
		}

		// Polar day or night: the sun is up all day when the declination points at this hemisphere.
		double declination = Declination(DateOnly.FromDateTime(utc.UtcDateTime));

		return Math.Sign(declination) == Math.Sign(latitude);
	}

	private static DateTimeOffset Instant(DateOnly date, double minutesUtc) =>
		new(
			DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddMinutes(minutesUtc),
			TimeSpan.Zero);

	/// <summary>The solar declination in radians (NOAA coefficients on the fractional year).</summary>
	private static double Declination(DateOnly date)
	{
		double gamma = FractionalYear(date);

		return 0.006918
			- 0.399912 * Math.Cos(gamma)
			+ 0.070257 * Math.Sin(gamma)
			- 0.006758 * Math.Cos(2 * gamma)
			+ 0.000907 * Math.Sin(2 * gamma)
			- 0.002697 * Math.Cos(3 * gamma)
			+ 0.00148 * Math.Sin(3 * gamma);
	}

	/// <summary>The equation of time in minutes (NOAA coefficients on the fractional year).</summary>
	private static double EquationOfTime(DateOnly date)
	{
		double gamma = FractionalYear(date);

		return 229.18
			* (0.000075
			+ 0.001868 * Math.Cos(gamma)
			- 0.032077 * Math.Sin(gamma)
			- 0.014615 * Math.Cos(2 * gamma)
			- 0.040849 * Math.Sin(2 * gamma));
	}

	/// <summary>Where the year stands, in radians; noon of the day (NOAA fractional year).</summary>
	private static double FractionalYear(DateOnly date) =>
		2 * Math.PI / 365 * (date.DayOfYear - 1);
}
