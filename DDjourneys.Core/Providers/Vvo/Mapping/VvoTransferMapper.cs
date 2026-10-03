using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Parsing;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// Transfer instructions (Footpath, StayForConnection, Mobility): classification, kind, waiting time.
/// </summary>
public static class VvoTransferMapper
{
	public static bool IsTransfer(
		VvoPartialRoute route)
	{
		string? type =
			route.Mot?.Type;


		if (string.Equals(
			type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}


		// A Footpath without regular stops is a transfer instruction.
		// It is represented by JourneyTransfer rather than JourneyLeg.
		if (string.Equals(
			type,
			"Footpath",
			StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}


		// Mobility instructions describe accessibility movement
		// inside a transfer, not passenger transport.
		return type?.StartsWith(
			"Mobility",
			StringComparison.OrdinalIgnoreCase)
			== true;
	}

	/// <summary>"StayForConnection": the connecting vehicle waits for the arriving one.</summary>
	public static bool IsEnsuredConnection(
		VvoPartialRoute route) =>
		string.Equals(
			route.Mot?.Type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase);

	public static JourneyTransfer MapTransfer(
		VvoPartialRoute route,
		JourneyLeg? previousLeg,
		int? previousLegIndex,
		JourneyLeg? nextLeg,
		int? nextLegIndex,
		IReadOnlyList<
			(double Latitude, double Longitude)> path)
	{
		StopTime? arrivalStop =
			previousLeg?
				.Stops
				.LastOrDefault();


		StopTime? departureStop =
			nextLeg?
				.Stops
				.FirstOrDefault();


		// A transfer partial route normally has no RegularStops.
		// Therefore its location must come from the surrounding
		// movement legs.
		Station? location =
			previousLeg?.To
			?? nextLeg?.From;


		if (location is null)
		{
			throw new InvalidOperationException(
				"VVO transfer has no identifiable location.");
		}


		DebugTransfer(
			route,
			departureStop
				?? nextLeg?
					.Stops
					.FirstOrDefault(),
			arrivalStop
				?? previousLeg?
					.Stops
					.LastOrDefault(),
			path.Count);


		return new JourneyTransfer
		{
			Location =
				location,

			Duration =
				TimeSpan.FromMinutes(
					Math.Max(
						0,
						route.Duration)),

			WaitingTime =
				DetermineWaitingTime(
					route,
					arrivalStop,
					departureStop),

			Kind =
				DetermineTransferKind(
					route,
					arrivalStop,
					departureStop),

			IsGuaranteed =
				IsEnsuredConnection(route)
				|| !route.ChangeoverEndangered,

			IsEnsured =
				IsEnsuredConnection(route),

			ProviderData =
				route,

			ArrivalPlatform =
				arrivalStop?.Platform
				?? previousLeg?.ArrivalPlatform,

			ArrivalPlatformKind =
				arrivalStop?.Platform is not null
					? arrivalStop.PlatformKind
					: previousLeg?.ArrivalPlatformKind
						?? PlatformKind.Unknown,

			DeparturePlatform =
				departureStop?.Platform
				?? nextLeg?.DeparturePlatform,

			DeparturePlatformKind =
				departureStop?.Platform is not null
					? departureStop.PlatformKind
					: nextLeg?.DeparturePlatformKind
						?? PlatformKind.Unknown,

			PreviousLegIndex =
				previousLegIndex,

			NextLegIndex =
				nextLegIndex,

			Path =
				path,

			Notices =
				VvoNoticeParser.Parse(
					route.Infos)
		};
	}

	public static TimeSpan? DetermineWaitingTime(
		VvoPartialRoute route,
		StopTime? arrival,
		StopTime? departure)
	{
		if (!string.Equals(
			route.Mot?.Type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}


		if (arrival?.EffectiveArrival is { } arrives
			&& departure?.EffectiveDeparture is { } departs
			&& departs >= arrives)
		{
			return departs - arrives;
		}


		return null;
	}

	public static TransferKind DetermineTransferKind(
		VvoPartialRoute route,
		StopTime? arrival,
		StopTime? departure)
	{
		string? type =
			route.Mot?.Type;


		if (string.Equals(
			type,
			"Footpath",
			StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.Walk;
		}


		if (type?.StartsWith(
			"Mobility",
			StringComparison.OrdinalIgnoreCase)
			== true)
		{
			return TransferKind.Accessibility;
		}


		if (string.Equals(
			type,
			"StayForConnection",
			StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.Waiting;
		}


		if (arrival?.Station.Id is { } arrivalId
			&& departure?.Station.Id is { } departureId
			&& !string.Equals(
				arrivalId,
				departureId,
				StringComparison.OrdinalIgnoreCase))
		{
			return TransferKind.Walk;
		}


		if (!string.Equals(
				arrival?.Platform,
				departure?.Platform,
				StringComparison.OrdinalIgnoreCase)
			|| (arrival?.Platform is not null
				&& arrival.PlatformKind != departure?.PlatformKind
				&& arrival.PlatformKind != PlatformKind.Unknown
				&& departure?.PlatformKind != PlatformKind.Unknown))
		{
			return TransferKind.PlatformChange;
		}


		return TransferKind.SameStop;
	}

	public static void DebugTransfer(
	VvoPartialRoute route,
	StopTime? departureStop,
	StopTime? arrivalStop,
	int pathCount)
	{
		System.Diagnostics.Debug.WriteLine(
			$"""
			[VVO TRANSFER]
			PartialRouteId: {route.PartialRouteId}
			Duration raw: {route.Duration} min
			Kind: {route.Mot?.Type} / {route.Mot?.Name}
			Stops: {route.RegularStops.Count}
			Path points: {pathCount}

			Previous:
			  Station: {arrivalStop?.Station.Name}
			  Id: {arrivalStop?.Station.Id}
			  Arrival: {arrivalStop?.ScheduledArrival:o}
			  Departure: {arrivalStop?.ScheduledDeparture:o}
			  Platform: {arrivalStop?.Platform}

			Next:
			  Station: {departureStop?.Station.Name}
			  Id: {departureStop?.Station.Id}
			  Arrival: {departureStop?.ScheduledArrival:o}
			  Departure: {departureStop?.ScheduledDeparture:o}
			  Platform: {departureStop?.Platform}

			Infos:
			  {string.Join(" | ", route.Infos)}
			""");
	}
}
