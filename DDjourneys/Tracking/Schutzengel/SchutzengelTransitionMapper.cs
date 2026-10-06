using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using static DDjourneys.Tracking.Schutzengel.SchutzengelWireCodes;
using static DDjourneys.Tracking.Schutzengel.SchutzengelNodeMapper;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Transitions between two movement legs: walks, accessibility steps, vehicle changes.
/// </summary>
internal static class SchutzengelTransitionMapper
{
	internal static IReadOnlyList<JsonObject> BuildTransitions(
		VvoRoute route,
		Journey journey,
		int movementIndex,
		int rawIndex,
		int? nextRawIndex)
	{
		var transferParts =
			new List<VvoPartialRoute>();


		if (nextRawIndex is { } nextIndex)
		{
			for (int i = rawIndex + 1;
				i < nextIndex;
				i++)
			{
				VvoPartialRoute candidate =
					route.PartialRoutes[i];


				if (IsMovement(candidate))
				{
					break;
				}


				transferParts.Add(
					candidate);
			}
		}


		JourneyTransfer[] journeyTransfers =
			[.. journey.Transfers
				.Where(
					transfer =>
						transfer.PreviousLegIndex
							== movementIndex
						&& transfer.NextLegIndex
							== movementIndex + 1)];


		var result =
			new List<JsonObject>();


		for (int index = 0;
			index < transferParts.Count;
			index++)
		{
			VvoPartialRoute transferPart =
				transferParts[index];


			JourneyTransfer? journeyTransfer =
				index < journeyTransfers.Length
					? journeyTransfers[index]
					: null;


			JsonArray path =
				journeyTransfer is not null
				&& journeyTransfer.Path.Count > 0
					? DDjourneys.Core.Serialization.Wire.Array(
						journeyTransfer.Path.Select(point => (JsonNode?)PointObject(point)))
					: [];


			result.Add(
				new JsonObject
				{
					["duration"] = Math.Max(0, transferPart.Duration),
					["sections"] =
						new JsonArray(
							new JsonObject
							{
								["type"] = TransitionType(transferPart.Mot?.Type),
								["category"] = TransportationCategoryTransition,
								["pathOnMap"] = path
							})
				});
		}


		if (result.Count == 0
			&& nextRawIndex is not null
			&& movementIndex + 1 < journey.Legs.Count)
		{
			JourneyLeg previous =
				journey.Legs[movementIndex];

			JourneyLeg next =
				journey.Legs[movementIndex + 1];


			if (string.Equals(
				previous.To.Id,
				next.From.Id,
				StringComparison.OrdinalIgnoreCase))
			{
				int syntheticDuration =
					SyntheticVehicleChangeDuration(
						previous,
						next);


				result.Add(
					new JsonObject
					{
						["duration"] = syntheticDuration,
						["sections"] =
							new JsonArray(
								new JsonObject
								{
									["type"] = TransitionTypeChangeVehicles,
									["category"] = TransportationCategoryTransition,
									["pathOnMap"] = new JsonArray()
								})
					});
			}
		}


		return result;
	}


	internal static int SyntheticVehicleChangeDuration(
		JourneyLeg previous,
		JourneyLeg next)
	{
		DateTimeOffset? previousTime =
			(previous.Stops.Count > 0 ? previous.Stops[^1] : null)
				?.RealtimeArrival
			?? (previous.Stops.Count > 0 ? previous.Stops[^1] : null)
				?.ScheduledArrival
			?? previous.EffectiveArrival;


		DateTimeOffset? nextTime =
			(next.Stops.Count > 0 ? next.Stops[0] : null)
				?.RealtimeDeparture
			?? (next.Stops.Count > 0 ? next.Stops[0] : null)
				?.ScheduledDeparture
			?? next.EffectiveDeparture;


		if (previousTime is not { } previousValue
			|| nextTime is not { } nextValue)
		{
			return 0;
		}


		return Math.Max(
			0,
			(int)
				Math.Truncate(
					(nextValue - previousValue)
						.TotalMinutes));
	}


	/// <summary>
	/// A ride. Classified exactly like the journey mapper does (transfer instructions first): a "StayForConnection"
	/// part may list stops, but it is the guarantee of the change, not a vehicle. Counting it as a ride shifted the
	/// legs (the second ride vanished and the plan showed "gesicherter Anschluss" as a line).
	/// </summary>
	internal static bool IsMovement(
		VvoPartialRoute route) =>
		route.RegularStops.Count > 0
		&& !DDjourneys.Core.Providers.Vvo.Mapping.VvoTransferMapper.IsTransfer(route);
}
