using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Maps VVO provider DTOs into DDjourneys domain models: orchestrates the sub-mappers.
/// </summary>
public static class VvoJourneyMapper
{
	public static IReadOnlyList<Journey> Map(
	VvoTripResponse response,
	Location? origin = null,
	Location? destination = null,
	DateTimeOffset? requested = null,
	bool arrival = false)
	{
		ArgumentNullException.ThrowIfNull(response);

		Station? originStation = VvoStopMapper.ToStation(origin);
		Station? destinationStation = VvoStopMapper.ToStation(destination);

		return response.Routes
			.Select(route => MapJourney(route, response.SessionId, originStation, destinationStation, requested, arrival))
			.Where(journey => journey is not null)
			.Select(journey => journey!)
			.ToArray();
	}

	private static Journey? MapJourney(
		VvoRoute route,
		string? sessionId,
		Station? origin,
		Station? destination,
		DateTimeOffset? requested,
		bool arrival)
	{
		VvoDebug.DumpRoute(route);

		var legs =
			new List<JourneyLeg>();

		var transfers =
			new List<JourneyTransfer>();

		var mappedParts =
			new List<
				(
					VvoPartialRoute Route,
					JourneyLeg? Leg,
					int? LegIndex
				)>();


		// First pass: map movement legs only.
		// Transfer/accessibility instructions remain out of Journey.Legs.
		foreach (VvoPartialRoute partialRoute
			in route.PartialRoutes)
		{
			DiagnosticLog.Write(
				$"""
				[VVO PARTIAL]
				Type={partialRoute.Mot?.Type}
				Name={partialRoute.Mot?.Name}
				Duration={partialRoute.Duration}
				Stops={partialRoute.RegularStops.Count}
				""");

			if (VvoTransferMapper.IsTransfer(partialRoute))
			{
				mappedParts.Add(
					(partialRoute, null, null));

				continue;
			}

			var path = VvoPathMapper.MapPath(route, partialRoute);

			JourneyLeg leg =
				VvoLegMapper.MapLeg(
					partialRoute,
					legs.LastOrDefault()?.To,
					VvoStopMapper.GetFirstStation(partialRoute),
					route.RouteCancelled,
					path);

			int legIndex =
				legs.Count;

			DiagnosticLog.Write(
	$"""
	[VVO LEG]
	Index: {legIndex}
	Type: {partialRoute.Mot?.Type}
	Name: {partialRoute.Mot?.Name}
	Duration: {partialRoute.Duration} min
	Path points:
	  {path.Count}
	FirstStop:
	  {partialRoute.RegularStops.FirstOrDefault()?.Name}
	  {partialRoute.RegularStops.FirstOrDefault()?.DepartureTime}
	LastStop:
	  {partialRoute.RegularStops.LastOrDefault()?.Name}
	  {partialRoute.RegularStops.LastOrDefault()?.ArrivalTime}
	""");

			legs.Add(leg);

			mappedParts.Add(
				(partialRoute, leg, legIndex));
		}


		// Nothing but a footpath (origin and destination are a short walk apart): the walk is the journey.
		bool walkOnly = false;

		if (legs.Count == 0)
		{
			JourneyLeg? walk =
				WalkOnly(route, origin, destination, requested, arrival);

			if (walk is null)
			{
				return null;
			}

			legs.Add(walk);
			walkOnly = true;
		}


		// Second pass: map transfer instructions with exact
		// surrounding leg indices.
		for (int i = 0;
			!walkOnly && i < mappedParts.Count;
			i++)
		{
			var part =
				mappedParts[i];

			if (!VvoTransferMapper.IsTransfer(part.Route))
			{
				continue;
			}


			JourneyLeg? previousLeg = null;
			int? previousLegIndex = null;

			JourneyLeg? nextLeg = null;
			int? nextLegIndex = null;


			for (int previous = i - 1;
				previous >= 0;
				previous--)
			{
				if (mappedParts[previous].Leg is { } leg)
				{
					previousLeg = leg;
					previousLegIndex =
						mappedParts[previous].LegIndex;

					break;
				}
			}


			for (int next = i + 1;
				next < mappedParts.Count;
				next++)
			{
				if (mappedParts[next].Leg is { } leg)
				{
					nextLeg = leg;
					nextLegIndex =
						mappedParts[next].LegIndex;

					break;
				}
			}


			transfers.Add(
	VvoTransferMapper.MapTransfer(
		part.Route,
		previousLeg,
		previousLegIndex,
		nextLeg,
		nextLegIndex,
		VvoPathMapper.MapPath(route, part.Route)));
		}


		if (legs.Count == 0)
		{
			return null;
		}

		DiagnosticLog.Write(
	$"""
	[VVO JOURNEY PATHS]
	Legs:
	{string.Join(
		Environment.NewLine,
		legs.Select(
			(l, i) =>
				$"  {i}: {l.Mode} {l.Line?.Name} Path={l.Path.Count}"))}

	Transfers:
	{string.Join(
		Environment.NewLine,
		transfers.Select(
			(t, i) =>
				$"  {i}: {t.Kind} Path={t.Path.Count}"))}
	""");

		return new Journey
		{
			From =
				legs[0].From,

			To =
				legs[^1].To,

			Origin =
				VvoStopMapper.WithPlaceFrom(
					origin,
					legs[0].From),

			Destination =
				VvoStopMapper.WithPlaceFrom(
					destination,
					legs[^1].To),

			Legs =
				legs,

			Transfers =
				transfers,

			Id =
				route.RouteId.ToString(
					System.Globalization.CultureInfo.InvariantCulture),

			ProviderId =
				VvoProviderInfo.Id,

			ProviderData =
				route,

			Fares =
				VvoFareMapper.Map(route),

			Context =
				string.IsNullOrWhiteSpace(sessionId)
					? null
					: sessionId,

			PlannedDuration =
				TimeSpan.FromMinutes(
					route.Duration)
		};
	}

	/// <summary>
	/// A route that consists of a single footpath. VVO describes it as a transfer without stops, so the leg is
	/// built from what is known: the searched places (or the stops the route lists), the route's duration and
	/// the time that was asked for.
	/// </summary>
	private static JourneyLeg? WalkOnly(
		VvoRoute route,
		Station? origin,
		Station? destination,
		DateTimeOffset? requested,
		bool arrival)
	{
		VvoPartialRoute[] parts = [.. route.PartialRoutes];

		VvoPartialRoute? footpath =
			parts.FirstOrDefault(
				part => string.Equals(part.Mot?.Type, "Footpath", StringComparison.OrdinalIgnoreCase));

		if (footpath is null
			|| parts.Any(part => !VvoTransferMapper.IsTransfer(part)))
		{
			return null;
		}

		StopTime[] listed =
			[.. parts
				.SelectMany(part => part.RegularStops)
				.Select(VvoStopMapper.MapStop)];

		Station? from = origin ?? listed.FirstOrDefault()?.Station;
		Station? to = destination ?? listed.LastOrDefault()?.Station;

		if (from is null
			|| to is null)
		{
			return null;
		}

		int minutes =
			parts.Sum(part => Math.Max(0, part.Duration));

		TimeSpan duration =
			TimeSpan.FromMinutes(
				minutes > 0
					? minutes
					: Math.Max(1, route.Duration));

		DateTimeOffset? start = listed.FirstOrDefault()?.ScheduledDeparture ?? listed.FirstOrDefault()?.ScheduledArrival;
		DateTimeOffset? end = listed.LastOrDefault()?.ScheduledArrival ?? listed.LastOrDefault()?.ScheduledDeparture;

		if (start is null
			&& end is null
			&& requested is { } asked)
		{
			if (arrival)
			{
				end = asked;
			}
			else
			{
				start = asked;
			}
		}

		start ??= end - duration;
		end ??= start + duration;

		var path = new List<(double Latitude, double Longitude)>();

		foreach (VvoPartialRoute part in parts)
		{
			path.AddRange(VvoPathMapper.MapPath(route, part));
		}

		return new JourneyLeg
		{
			Mode = TransitMode.Walk,
			From = from,
			To = to,
			Path = path,
			ScheduledDeparture = start,
			ScheduledArrival = end,
			Id = footpath.PartialRouteId.ToString(System.Globalization.CultureInfo.InvariantCulture),
			ProviderData = footpath,
			Notices = VvoNoticeParser.Parse(footpath.Infos)
		};
	}
}
