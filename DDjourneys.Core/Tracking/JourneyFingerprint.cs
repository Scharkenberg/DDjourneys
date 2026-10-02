using DDjourneys.Core.Models;

namespace DDjourneys.Core.Tracking;

/// <summary>
/// Provider-independent identity of a journey: scheduled start of its first ride, scheduled end of
/// its last ride and the ride lines in between. Built from planned values only, so real-time
/// changes do not alter it, and from nothing but times and line names, which every provider
/// representation of the same journey agrees on (station ids do not always).
/// </summary>
public static class JourneyFingerprint
{
	public static string? Of(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		List<JourneyLeg> rides = journey.Legs.Where(IsRide).ToList();

		if (rides.Count == 0)
		{
			return null;
		}

		JourneyLeg first = rides[0];
		JourneyLeg last = rides[^1];

		return Compose(
			(first.Stops.FirstOrDefault()?.ScheduledDeparture ?? first.ScheduledDeparture)?.ToUnixTimeMilliseconds(),
			(last.Stops.LastOrDefault()?.ScheduledArrival ?? last.ScheduledArrival)?.ToUnixTimeMilliseconds(),
			rides.Select(ride => ride.Line?.Name));
	}

	public static string? Compose(
		long? departureMilliseconds,
		long? arrivalMilliseconds,
		IEnumerable<string?> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);

		if (departureMilliseconds is null || arrivalMilliseconds is null)
		{
			return null;
		}

		string joined =
			string.Join(
				"|",
				lines.Select(line => (line ?? string.Empty).Trim().ToUpperInvariant()));

		return $"{departureMilliseconds}>{arrivalMilliseconds}#{joined}";
	}

	private static bool IsRide(JourneyLeg leg) =>
		leg.Mode is not (TransitMode.Walk or TransitMode.Taxi or TransitMode.OnDemand);
}
