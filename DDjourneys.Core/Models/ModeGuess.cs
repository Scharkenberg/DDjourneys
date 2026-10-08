using System.Text.RegularExpressions;

namespace DDjourneys.Core.Models;

/// <summary>
/// The mode of a ride from its line name alone, for lists that only keep names (followed journeys made before the
/// rides were stored). A guess, used only when nothing better is known: S-Bahn lines, regional and long-distance trains, subways,
/// ferries and cable cars by their prefix, the rest by the Dresden numbering (1 to 13 trams, higher numbers buses).
/// </summary>
public static partial class ModeGuess
{
	public static TransitMode OfLine(string? line)
	{
		string name = (line ?? string.Empty).Trim();

		if (name.Length == 0)
		{
			return TransitMode.Bus;
		}

		if (SuburbanPattern().IsMatch(name))
		{
			return TransitMode.SuburbanRail;
		}

		if (RegionalPattern().IsMatch(name))
		{
			return TransitMode.RegionalTrain;
		}

		if (LongDistancePattern().IsMatch(name))
		{
			return TransitMode.LongDistanceTrain;
		}

		if (SubwayPattern().IsMatch(name))
		{
			return TransitMode.Subway;
		}

		if (FerryPattern().IsMatch(name))
		{
			return TransitMode.Ferry;
		}

		if (name is "Standseilbahn" or "Schwebebahn" or "SB" or "SWB")
		{
			return TransitMode.CableCar;
		}

		return int.TryParse(name, out int number) && number is >= 1 and <= 13
			? TransitMode.Tram
			: TransitMode.Bus;
	}

	[GeneratedRegex(@"^S\s?\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex SuburbanPattern();

	[GeneratedRegex(@"^(RE|RB|FEX|MRB|TL)\s?\d*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex RegionalPattern();

	[GeneratedRegex(@"^(IC|ICE|EC|EN|RJ|RJX)\s?\d*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex LongDistancePattern();

	[GeneratedRegex(@"^U\s?\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex SubwayPattern();

	[GeneratedRegex(@"^F\s?\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex FerryPattern();
}
