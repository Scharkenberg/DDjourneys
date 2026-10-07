using DDjourneys.Core.Models;

namespace DDjourneys.Core.Services;

/// <summary>
/// A run of legs of a journey with the transfers that belong to them. Transfer indexes are local to the run;
/// null means "outside the run" (the walk before the first leg, the walk after the last one, or the joint to a
/// neighbouring run).
/// </summary>
public sealed record JourneySegment(
	IReadOnlyList<JourneyLeg> Legs,
	IReadOnlyList<JourneyTransfer> Transfers)
{
	public static JourneySegment Empty { get; } = new([], []);

	/// <summary>The whole of a journey as one run.</summary>
	public static JourneySegment Of(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		return new(journey.Legs, journey.Transfers);
	}

	/// <summary>One leg and nothing else.</summary>
	public static JourneySegment Of(JourneyLeg leg)
	{
		ArgumentNullException.ThrowIfNull(leg);

		return new([leg], []);
	}
}

/// <summary>
/// Builds a journey out of pieces of other journeys: the legs of an earlier connection, a replaced ride, a re-planned
/// remainder. The result is the provider's data only as far as its pieces are; it carries no session, route id or fare
/// of its own, since those belong to the journeys it was cut from.
/// </summary>
public static class JourneySplice
{
	/// <summary>
	/// The transfers of <paramref name="journey"/> that belong to the legs from <paramref name="from"/> up to (not
	/// including) <paramref name="to"/>. A transfer belongs to the run that holds the leg after it (the walk after the
	/// last leg belongs to the last run); one that comes from a leg before the run becomes its leading joint.
	/// </summary>
	public static JourneySegment Slice(Journey journey, int from, int to)
	{
		ArgumentNullException.ThrowIfNull(journey);

		from = Math.Clamp(from, 0, journey.Legs.Count);
		to = Math.Clamp(to, from, journey.Legs.Count);

		if (from == to)
		{
			return JourneySegment.Empty;
		}

		var transfers = new List<JourneyTransfer>();

		foreach (JourneyTransfer transfer in journey.Transfers)
		{
			bool belongs =
				transfer.NextLegIndex is { } next
					? next >= from && next < to
					: transfer.PreviousLegIndex is { } previous && previous >= from && previous < to;

			if (!belongs)
			{
				continue;
			}

			transfers.Add(
				Reindex(
					transfer,
					transfer.PreviousLegIndex is { } p && p >= from ? p - from : null,
					transfer.NextLegIndex is { } n ? n - from : null));
		}

		return new([.. journey.Legs.Skip(from).Take(to - from)], transfers);
	}

	/// <summary>
	/// Joins the runs in order. A transfer that leads into a run from outside is tied to the last leg before it, one
	/// that leads out of a run to the first leg after it. Use <see cref="Renewed"/> on a run whose neighbours were
	/// replaced, so that the provider's "ensured" flag and waiting time of the old pair are not kept.
	/// </summary>
	public static Journey Assemble(
		Journey template,
		IReadOnlyList<JourneySegment> segments,
		bool keepFares = false,
		IReadOnlyList<string>? notices = null,
		Station? origin = null,
		Station? destination = null)
	{
		ArgumentNullException.ThrowIfNull(template);
		ArgumentNullException.ThrowIfNull(segments);

		var legs = new List<JourneyLeg>();
		var transfers = new List<JourneyTransfer>();

		JourneySegment[] used = [.. segments.Where(segment => segment.Legs.Count > 0)];

		for (int s = 0; s < used.Length; s++)
		{
			JourneySegment segment = used[s];
			int offset = legs.Count;
			bool first = s == 0;
			bool last = s == used.Length - 1;

			foreach (JourneyTransfer transfer in segment.Transfers)
			{
				int? previous =
					transfer.PreviousLegIndex is { } p
						? p + offset
						: first ? null : offset - 1;

				int? next =
					transfer.NextLegIndex is { } n
						? n + offset
						: last ? null : offset + segment.Legs.Count;

				transfers.Add(Reindex(transfer, previous, next));
			}

			legs.AddRange(segment.Legs);
		}

		return new Journey
		{
			Legs = legs,
			From = legs.Count > 0 ? legs[0].From : template.From,
			To = legs.Count > 0 ? legs[^1].To : template.To,
			Origin = origin ?? template.Origin,
			Destination = destination ?? template.Destination,
			ProviderId = template.ProviderId,
			Transfers = transfers,
			Notices = notices ?? template.Notices,
			Fares = keepFares ? template.Fares : [],
			Id = null,
			Context = null,
			ProviderData = null,
			PlannedDuration = null
		};
	}

	/// <summary>
	/// A copy of the transfer for new neighbours: the planned waiting time and the provider's guarantee flags described
	/// the old pair of legs, so they are reset when <paramref name="changed"/> (the legs next to it were replaced).
	/// </summary>
	public static JourneyTransfer Reindex(
		JourneyTransfer transfer,
		int? previous,
		int? next,
		bool changed = false) =>
		new()
		{
			Location = transfer.Location,
			PreviousLegIndex = previous,
			NextLegIndex = next,
			Duration = transfer.Duration,
			WaitingTime = changed ? null : transfer.WaitingTime,
			Kind = transfer.Kind,
			Path = transfer.Path,
			IsGuaranteed = changed || transfer.IsGuaranteed,
			IsEnsured = !changed && transfer.IsEnsured,
			ProviderData = transfer.ProviderData,
			ArrivalPlatform = transfer.ArrivalPlatform,
			ArrivalPlatformKind = transfer.ArrivalPlatformKind,
			DeparturePlatform = transfer.DeparturePlatform,
			DeparturePlatformKind = transfer.DeparturePlatformKind,
			From = transfer.From,
			To = transfer.To,
			Notices = transfer.Notices
		};

	/// <summary>The same segment with the connection flags of every transfer reset (see <see cref="Reindex"/>).</summary>
	public static JourneySegment Renewed(JourneySegment segment) =>
		new(
			segment.Legs,
			[.. segment.Transfers.Select(transfer => Reindex(transfer, transfer.PreviousLegIndex, transfer.NextLegIndex, changed: true))]);

	/// <summary>The same run with the flags of its leading joint (the transfer from the leg before it) reset.</summary>
	public static JourneySegment RenewedLeading(JourneySegment segment) =>
		new(
			segment.Legs,
			[.. segment.Transfers.Select(
				transfer => transfer.PreviousLegIndex is null
					? Reindex(transfer, null, transfer.NextLegIndex, changed: true)
					: transfer)]);
}
