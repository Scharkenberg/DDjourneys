using System.Globalization;
using System.Text.Json;
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
	internal static IReadOnlyList<object> BuildTransitions(
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
			journey.Transfers
				.Where(
					transfer =>
						transfer.PreviousLegIndex
							== movementIndex
						&& transfer.NextLegIndex
							== movementIndex + 1)
				.ToArray();


		var result =
			new List<object>();


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


			object[] path =
				journeyTransfer is not null
				&& journeyTransfer.Path.Count > 0
					? journeyTransfer.Path
						.Select(PointObject)
						.ToArray()
					: Array.Empty<object>();


			result.Add(
				new
				{
					duration =
						Math.Max(
							0,
							transferPart.Duration),

					sections =
						new[]
						{
							new
							{
								type =
									TransitionType(
										transferPart.Mot?.Type),

								category =
									TransportationCategoryTransition,

								pathOnMap =
									path
							}
						}
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
					new
					{
						duration =
							syntheticDuration,

						sections =
							new[]
							{
								new
								{
									type =
										TransitionTypeChangeVehicles,

									category =
										TransportationCategoryTransition,

									pathOnMap =
										Array.Empty<object>()
								}
							}
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
			previous.Stops
				.LastOrDefault()
				?.RealtimeArrival
			?? previous.Stops
				.LastOrDefault()
				?.ScheduledArrival
			?? previous.EffectiveArrival;


		DateTimeOffset? nextTime =
			next.Stops
				.FirstOrDefault()
				?.RealtimeDeparture
			?? next.Stops
				.FirstOrDefault()
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


	internal static bool IsMovement(
		VvoPartialRoute route) =>
		route.RegularStops.Count > 0;
}
