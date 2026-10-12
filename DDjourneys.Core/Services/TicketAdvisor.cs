using System.Globalization;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>How far the provider's own fares say a Deutschlandticket covers a journey.</summary>
public enum TicketCoverage
{
	/// <summary>The fares carry no zones: whether the ticket covers the journey cannot be told, so nothing is claimed.</summary>
	Unknown = 0,

	/// <summary>The provider prices the journey by its own fare zones: regional transport inside the network, which the ticket covers.</summary>
	WithinVerbund,

	/// <summary>The journey rides a long-distance train, which the ticket does not cover.</summary>
	LongDistance
}

/// <summary>
/// What can be told about the tickets of one journey: the fare zones it crosses, the single-versus-day
/// comparison, the coverage by a Deutschlandticket and its price against single rides. Numbers and
/// verdicts only - the app puts them into words in one place.
/// </summary>
public sealed record TicketAdvice
{
	/// <summary>The fare zones the journey crosses, when the quoted fares say (their names or their count).</summary>
	public int? ZoneCount { get; init; }

	/// <summary>The single ticket the passenger would buy, and its currency.</summary>
	public decimal? SinglePrice { get; init; }

	public string? SingleCurrency { get; init; }

	/// <summary>The day ticket for the same journey, when the provider quotes one.</summary>
	public decimal? DayPrice { get; init; }

	public string? DayCurrency { get; init; }

	/// <summary>
	/// The ride from which the day ticket is cheaper than single tickets: a day ticket that costs as much
	/// as two singles wins from the third ride, one that costs less than a single from the first.
	/// </summary>
	public int? DayWinsFromRide { get; init; }

	/// <summary>Single rides after which a Deutschlandticket at its public monthly price has paid for itself.</summary>
	public int? DeutschlandTicketRides { get; init; }

	/// <summary>What the quoted fares say about where the journey goes.</summary>
	public TicketCoverage Coverage { get; init; }

	/// <summary>
	/// A BahnCard 25 price computed as three quarters of the single fare - an estimate to be labelled as
	/// one, never a quoted fare (the provider's answer carries no conditional prices).
	/// </summary>
	public decimal? BahnCard25Estimate { get; init; }
}

/// <summary>
/// The advice behind the ticket card of a journey. Everything comes from the fares the provider quotes
/// for the journey (its own, or the ones the <see cref="FareBorrower"/> borrowed for it) except two
/// numbers that are the app's own: the public monthly price of the Deutschlandticket and the BahnCard 25
/// estimate, both of which are marked as computed where they are shown.
/// </summary>
public static partial class TicketAdvisor
{
	/// <summary>
	/// The public monthly price of the Deutschlandticket, in euros. The owner of the app adjusts this
	/// constant when the price changes; no other place knows it.
	/// </summary>
	public const decimal DeutschlandTicketPrice = 58m;

	/// <summary>The BahnCard 25 estimate is the single fare minus this share (25 per cent off).</summary>
	private const decimal BahnCard25Share = 0.25m;

	/// <summary>What the quoted fares say about the zones, the tickets and the Deutschlandticket of one journey.</summary>
	public static TicketAdvice Advise(
		Journey journey,
		IReadOnlyList<JourneyFare> fares,
		PassengerCategory passenger)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(fares);

		JourneyFare? single = FareChoice.Preferred(
			fares.Where(fare => fare.Kind == FareKind.Single),
			passenger);

		JourneyFare? day = FareChoice.Preferred(
			fares.Where(fare => fare.Kind == FareKind.Day),
			passenger);

		int? zones =
			ZoneCountOf(single)
			?? ZoneCountOf(day)
			?? fares.Select(ZoneCountOf).FirstOrDefault(count => count.HasValue);

		TicketCoverage coverage =
			journey.Legs.Any(leg => leg.Mode == TransitMode.LongDistanceTrain)
				? TicketCoverage.LongDistance
				: zones is not null
					? TicketCoverage.WithinVerbund
					: TicketCoverage.Unknown;

		return new TicketAdvice
		{
			ZoneCount = zones,

			SinglePrice = single?.Price,
			SingleCurrency = single?.Currency,
			DayPrice = day?.Price,
			DayCurrency = day?.Currency,

			DayWinsFromRide =
				single?.Price is > 0 and var one && day?.Price is > 0 and var all
					? (int)Math.Floor(all / one) + 1
					: null,

			// A journey over a long-distance train is not fully covered, so its break-even is not said either.
			DeutschlandTicketRides =
				coverage == TicketCoverage.LongDistance
					? null
					: single?.Price is > 0 and var each
						? (int)Math.Ceiling(DeutschlandTicketPrice / each)
						: null,

			Coverage = coverage,

			BahnCard25Estimate =
				passenger is PassengerCategory.Adult or PassengerCategory.Senior
				&& coverage != TicketCoverage.LongDistance
				&& single?.Price is > 0 and var full
					? Math.Round(full * (1m - BahnCard25Share), 2, MidpointRounding.AwayFromZero)
					: null
		};
	}

	/// <summary>
	/// The zones a fare says the journey crosses: first the zone names it lists ("Dresden, Radebeul" are
	/// two), then its count as words ("1 Tarifzone"). Everything else (a tariff level, a validity) is not
	/// a zone count.
	/// </summary>
	private static int? ZoneCountOf(JourneyFare? fare)
	{
		if (fare is null)
		{
			return null;
		}

		string[] names =
			[.. (fare.Zones ?? string.Empty)
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(zone => zone.Length > 0)
				.Distinct()];

		if (names.Length > 0)
		{
			return names.Length;
		}

		Match count = ZoneWords().Match(fare.Description ?? string.Empty);

		return count.Success
			&& int.TryParse(count.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zones)
			&& zones > 0
				? zones
				: null;
	}

	/// <summary>"1 Tarifzone", "2 zones" - a number directly before a word about zones.</summary>
	[GeneratedRegex(@"^\s*(\d+)\s*\S*zone", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex ZoneWords();
}
