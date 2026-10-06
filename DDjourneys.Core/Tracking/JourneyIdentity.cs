using System.Globalization;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Tracking;

/// <summary>One ride of a <see cref="JourneyIdentity"/>: planned line and planned times.</summary>
public sealed record RideIdentity(
	string Line,
	long? DepartureMilliseconds,
	long? ArrivalMilliseconds,
	string? FromStopKey = null,
	string? ToStopKey = null);

/// <summary>
/// What makes a journey the same journey: provider, endpoints, ride lines and planned leg timing.
/// Built from planned values only, so real-time changes never alter it.
/// <para>
/// <see cref="Key"/> is the portable, persisted match key (first departure, last arrival and the line
/// sequence): every representation of the same journey agrees on it, including the Schutzengel raw
/// data, which knows nothing else. <see cref="IsSameAs"/> adds the stricter checks (provider, stop
/// keys, leg timing) wherever both sides know the respective values.
/// </para>
/// </summary>
public sealed record JourneyIdentity
{
	public string ProviderId { get; init; } = string.Empty;

	public IReadOnlyList<RideIdentity> Rides { get; init; } = [];

	/// <summary>Planned departure of the first ride (UTC milliseconds).</summary>
	public long? DepartureMilliseconds => Rides.Count == 0 ? null : Rides[0].DepartureMilliseconds;

	/// <summary>Planned arrival of the last ride (UTC milliseconds).</summary>
	public long? ArrivalMilliseconds => Rides.Count == 0 ? null : Rides[^1].ArrivalMilliseconds;

	/// <summary>Stop key of the first boarding, when the provider gave one.</summary>
	public string? OriginKey => Rides.Count == 0 ? null : Rides[0].FromStopKey;

	/// <summary>Stop key of the last alighting, when the provider gave one.</summary>
	public string? DestinationKey => Rides.Count == 0 ? null : Rides[^1].ToStopKey;

	/// <summary>The persisted match key; null when the journey has no ride or no planned times.</summary>
	public string? Key =>
		Compose(
			DepartureMilliseconds,
			ArrivalMilliseconds,
			Rides.Select(ride => ride.Line));

	public static JourneyIdentity? Of(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		List<JourneyLeg> rides = [.. journey.Rides];

		if (rides.Count == 0)
		{
			return null;
		}

		var identity =
			new JourneyIdentity
			{
				ProviderId = journey.ProviderId,
				Rides = [.. rides.Select(ToRide)]
			};

		return identity.Key is null ? null : identity;
	}

	/// <summary>
	/// True when both describe the same journey. A value one side does not know (empty provider,
	/// missing stop key, missing time) never counts against it; a value both know must agree.
	/// </summary>
	public bool IsSameAs(JourneyIdentity? other)
	{
		if (other is null
			|| Key is not { } key
			|| !string.Equals(key, other.Key, StringComparison.Ordinal))
		{
			return false;
		}

		if (ProviderId.Length > 0
			&& other.ProviderId.Length > 0
			&& !string.Equals(ProviderId, other.ProviderId, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!Agree(OriginKey, other.OriginKey) || !Agree(DestinationKey, other.DestinationKey))
		{
			return false;
		}

		// Same key means the same number of rides (the lines are part of it).
		for (int i = 0; i < Rides.Count; i++)
		{
			if (!Agree(Rides[i].DepartureMilliseconds, other.Rides[i].DepartureMilliseconds)
				|| !Agree(Rides[i].ArrivalMilliseconds, other.Rides[i].ArrivalMilliseconds))
			{
				return false;
			}
		}

		return true;
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
				lines.Select(Normalize));

		return string.Create(
			CultureInfo.InvariantCulture,
			$"{departureMilliseconds}>{arrivalMilliseconds}#{joined}");
	}

	public static string Normalize(string? line) =>
		(line ?? string.Empty).Trim().ToUpperInvariant();

	private static RideIdentity ToRide(JourneyLeg ride)
	{
		StopTime? first = ride.Stops.Count > 0 ? ride.Stops[0] : null;
		StopTime? last = ride.Stops.Count > 0 ? ride.Stops[^1] : null;

		return new RideIdentity(
			Normalize(ride.Line?.Name),
			(first?.ScheduledDeparture ?? ride.ScheduledDeparture)?.ToUnixTimeMilliseconds(),
			(last?.ScheduledArrival ?? ride.ScheduledArrival)?.ToUnixTimeMilliseconds(),
			NullIfEmpty(ride.From.StopKey),
			NullIfEmpty(ride.To.StopKey));
	}

	private static string? NullIfEmpty(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value;

	private static bool Agree(string? left, string? right) =>
		left is null
		|| right is null
		|| string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	private static bool Agree(long? left, long? right) =>
		left is null
		|| right is null
		|| left == right;
}
