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
		Location? destination = null)
	{
		ArgumentNullException.ThrowIfNull(response);

		Station? originStation =
			VvoStopMapper.ToStation(origin);

		Station? destinationStation =
			VvoStopMapper.ToStation(destination);

		return response.Routes
			.Select(
				route =>
					MapJourney(
						route,
						response.SessionId,
						originStation,
						destinationStation))
			.ToArray();
	}

	private static Journey MapJourney(
		VvoRoute route,
		string? sessionId,
		Station? origin,
		Station? destination)
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
			System.Diagnostics.Debug.WriteLine(
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

			System.Diagnostics.Debug.WriteLine(
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


		// Second pass: map transfer instructions with exact
		// surrounding leg indices.
		for (int i = 0;
			i < mappedParts.Count;
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
			throw new InvalidOperationException(
				"VVO route contains no movement legs.");
		}

		System.Diagnostics.Debug.WriteLine(
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

			Context =
				string.IsNullOrWhiteSpace(sessionId)
					? null
					: sessionId,

			PlannedDuration =
				TimeSpan.FromMinutes(
					route.Duration)
		};
	}
}
