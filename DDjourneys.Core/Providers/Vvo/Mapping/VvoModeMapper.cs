using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Transport mode and occupancy vocabulary of VVO.
/// </summary>
public static class VvoModeMapper
{
	public static TransitMode MapMode(
		VvoMot? mot)
	{
		if (mot is null)
		{
			return TransitMode.Unknown;
		}


		if (string.Equals(
			mot.Type,
			"Footpath",
			StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.Walk;
		}


		string value =
			$"{mot.Type} {mot.Name}"
				.ToLowerInvariant();


		if (value.Contains("tram"))
		{
			return TransitMode.Tram;
		}


		if (value.Contains("bus"))
		{
			return TransitMode.Bus;
		}


		if (value.Contains("s-bahn")
			|| value.Contains("suburban")
			|| string.Equals(
				mot.Type,
				"RapidTransit",
				StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.SuburbanRail;
		}


		if (string.Equals(
			mot.Type,
			"Train",
			StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.RegionalTrain;
		}


		if (value.Contains("ferry"))
		{
			return TransitMode.Ferry;
		}


		if (value.Contains("cableway")
			|| value.Contains("cablecar")
			|| value.Contains("cable car"))
		{
			return TransitMode.CableCar;
		}


		if (value.Contains("taxi"))
		{
			return TransitMode.Taxi;
		}


		return TransitMode.Unknown;
	}

	public static OccupancyLevel MapOccupancy(
		string? occupancy)
	{
		if (string.IsNullOrWhiteSpace(
			occupancy))
		{
			return OccupancyLevel.Unknown;
		}


		return occupancy
			.Trim()
			.ToLowerInvariant()
			switch
		{
			"verylow" or "manyseats" =>
				OccupancyLevel.VeryLow,

			"low" or "fewseats" =>
				OccupancyLevel.Low,

			"medium" =>
				OccupancyLevel.Medium,

			"high" or "standingonly" =>
				OccupancyLevel.High,

			"full" =>
				OccupancyLevel.Full,

			"veryhigh" or "overloaded" =>
				OccupancyLevel.Overloaded,

			_ =>
				OccupancyLevel.Unknown
		};
	}
}
