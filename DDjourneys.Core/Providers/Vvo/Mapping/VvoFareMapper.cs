using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// The tickets of a VVO route. The trip planner quotes two prices for the journey: the single ticket
/// (<c>Price</c>) and the day ticket (<c>PriceDayTicket</c>), each with the fare zones it covers; the
/// conditions come as <c>TicketNotes</c>. Weekly, monthly, reduced and Deutschland tickets are not part of the
/// answer, so they are not invented here.
/// </summary>
public static partial class VvoFareMapper
{
	public static IReadOnlyList<JourneyFare> Map(
		VvoRoute route)
	{
		ArgumentNullException.ThrowIfNull(route);

		var fares = new List<JourneyFare>();

		if (ParsePrice(route.Price) is { } single)
		{
			fares.Add(
				new JourneyFare
				{
					Name = "Einzelfahrt",
					Kind = FareKind.Single,
					Price = single,
					Zones = CleanZones(route.FareZoneNames),
					Description = Level(route.NumberOfFareZones),
					Notes = Clean(route.TicketNotes)
				});
		}

		if (ParsePrice(route.PriceDayTicket) is { } day)
		{
			fares.Add(
				new JourneyFare
				{
					Name = "Tageskarte",
					Kind = FareKind.Day,
					Price = day,
					Zones = CleanZones(route.FareZoneNamesDayTicket ?? route.FareZoneNames),
					Description = Level(route.NumberOfFareZonesDayTicket)
				});
		}

		return fares;
	}

	/// <summary>"2,70" or "2.70" (also "2,70 €"): the decimal, or null when there is no usable price.</summary>
	public static decimal? ParsePrice(
		string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		string number =
			new string(
				[.. text.Where(character => char.IsAsciiDigit(character) || character is ',' or '.')])
				.Replace(',', '.');

		return decimal.TryParse(
			number,
			NumberStyles.Number,
			CultureInfo.InvariantCulture,
			out decimal value)
			&& value > 0
				? value
				: null;
	}

	private static string? Level(
		string? text) =>
		string.IsNullOrWhiteSpace(text)
			? null
			: text.Trim();

	private static string? Clean(
		string? text) =>
		string.IsNullOrWhiteSpace(text)
			? null
			: text.ReplaceLineEndings(" ").Trim();

	/// <summary>"TZ Dresden (1), TZ Radebeul (2)" -> "Dresden, Radebeul".</summary>
	[System.Text.RegularExpressions.GeneratedRegex(@"^TZ\s|\s*\(\d+\)")]
	private static partial System.Text.RegularExpressions.Regex ZoneNoise();

	private static string? CleanZones(
		string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		string[] zones =
			[.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(zone => ZoneNoise().Replace(zone, string.Empty).Trim())
				.Where(zone => zone.Length > 0)
				.Distinct()];

		return zones.Length == 0
			? null
			: string.Join(", ", zones);
	}
}
