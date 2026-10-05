namespace DDjourneys.Core.Models;

/// <summary>Picks the ticket a card or a share should name.</summary>
public static class FareChoice
{
	/// <summary>
	/// The ticket to show for <paramref name="passenger"/>: a priced ticket for exactly that passenger first, then
	/// one without a passenger statement (providers that quote only the normal price mean the adult), then the adult
	/// ticket, then anything priced. Within that, single tickets before other kinds before day tickets, cheapest first.
	/// </summary>
	public static JourneyFare? Preferred(IEnumerable<JourneyFare> fares, PassengerCategory passenger)
	{
		ArgumentNullException.ThrowIfNull(fares);

		JourneyFare[] priced = [.. fares.Where(fare => fare.Price is not null)];

		static bool For(JourneyFare fare, PassengerCategory who) =>
			fare.Passengers.Contains(who);

		static bool Unspecified(JourneyFare fare) =>
			fare.Passengers.Count == 0;

		IEnumerable<JourneyFare> pool =
			Pick(priced, fare => For(fare, passenger))
			?? (passenger == PassengerCategory.Adult
				? Pick(priced, Unspecified)
				: null)
			?? Pick(priced, Unspecified)
			?? Pick(priced, fare => For(fare, PassengerCategory.Adult))
			?? priced;

		return pool
			.OrderBy(fare => fare.Kind == FareKind.Single ? 0 : fare.Kind == FareKind.Day ? 2 : 1)
			.ThenBy(fare => fare.Price)
			.FirstOrDefault();
	}

	private static JourneyFare[]? Pick(JourneyFare[] fares, Func<JourneyFare, bool> match)
	{
		JourneyFare[] matching = [.. fares.Where(match)];

		return matching.Length > 0
			? matching
			: null;
	}
}
