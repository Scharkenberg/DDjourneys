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

		// A ticket the planner names beyond the two prices: shown as the provider words it, and only when
		// it is not one of them (the same kind at the same price). A list that carries nothing beyond them -
		// the observed answer - leaves the fares exactly the two.
		foreach (VvoTicket ticket in route.Tickets)
		{
			if (string.IsNullOrWhiteSpace(ticket.Name)
				|| ParsePrice(ticket.Price) is not { } price)
			{
				continue;
			}

			FareKind kind = KindOf(ticket.Name);

			if (fares.Any(fare => fare.Kind == kind && fare.Price == price))
			{
				continue;
			}

			fares.Add(
				new JourneyFare
				{
					Name = ticket.Name.Trim(),
					Kind = kind,
					Price = price,
					Zones = CleanZones(ticket.FareZoneNames),
					Description = Level(ticket.NumberOfFareZones)
				});
		}

		return fares;
	}

	/// <summary>A ticket the planner names: a day ticket when its name says so, a single ticket likewise, anything else as it is.</summary>
	private static FareKind KindOf(string name)
	{
		string words = name.ToLowerInvariant();

		return words.Contains("tages") || words.Contains("day")
			? FareKind.Day
			: words.Contains("einzel") || words.Contains("single")
				? FareKind.Single
				: FareKind.Other;
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
