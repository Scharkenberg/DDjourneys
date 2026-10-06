using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using System.Text.Json.Nodes;

namespace DDjourneys.Support;

/// <summary>
/// The walks at both ends of a followed journey: from where the traveller starts to the first stop, and from the last
/// stop to the destination. The tracking service follows vehicles only, so these are kept here, written when the journey
/// is followed and dropped when it no longer is, and added to the live progress as footpaths (see <c>TripTimeline.SetWalks</c>).
/// JSON by hand (no reflection).
/// </summary>
public sealed record FollowedWalk(
	int LeadSeconds,
	string? From,
	double? FromLatitude,
	double? FromLongitude,
	int TrailSeconds,
	string? To,
	double? ToLatitude,
	double? ToLongitude)
{
	public bool IsEmpty =>
		LeadSeconds <= 0
		&& TrailSeconds <= 0;

	/// <summary>
	/// The walks of a journey: the transfers before the first and after the last ride, plus any walking legs there.
	/// Null for a journey without a ride (walking only: nothing for the service to follow).
	/// </summary>
	public static FollowedWalk? Of(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		int first = -1;
		int last = -1;

		for (int i = 0; i < journey.Legs.Count; i++)
		{
			if (journey.Legs[i].IsRide)
			{
				first = first < 0 ? i : first;
				last = i;
			}
		}

		if (first < 0)
		{
			return null;
		}

		static double Seconds(JourneyLeg leg) =>
			leg.ScheduledDeparture is { } start && leg.ScheduledArrival is { } end && end > start
				? (end - start).TotalSeconds
				: 0;

		double lead =
			journey.Transfers.Where(item => item.PreviousLegIndex is null).Sum(item => item.Duration.TotalSeconds)
			+ journey.Legs.Take(first).Sum(Seconds);

		double trail =
			journey.Transfers.Where(item => item.NextLegIndex is null).Sum(item => item.Duration.TotalSeconds)
			+ journey.Legs.Skip(last + 1).Sum(Seconds);

		Station? origin = journey.Origin ?? journey.From;
		Station? destination = journey.Destination ?? journey.To;

		return new FollowedWalk(
			(int)Math.Round(lead),
			origin?.Name,
			origin?.Latitude,
			origin?.Longitude,
			(int)Math.Round(trail),
			destination?.Name,
			destination?.Latitude,
			destination?.Longitude);
	}
}

/// <summary>Storage of <see cref="FollowedWalk"/> per followed journey (preferences, one JSON object).</summary>
public static class FollowedWalks
{
	private const string Key = "tracking.walks";

	public static void Save(string planId, Journey journey)
	{
		try
		{
			JsonObject all = Read();

			if (FollowedWalk.Of(journey) is { IsEmpty: false } walk)
			{
				all[planId] =
					new JsonObject
					{
						["Lead"] = walk.LeadSeconds,
						["From"] = walk.From,
						["FromLat"] = walk.FromLatitude,
						["FromLon"] = walk.FromLongitude,
						["Trail"] = walk.TrailSeconds,
						["To"] = walk.To,
						["ToLat"] = walk.ToLatitude,
						["ToLon"] = walk.ToLongitude
					};
			}
			else
			{
				all.Remove(planId);
			}

			Preferences.Default.Set(Key, all.ToJsonString());
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Remembering the walks of a followed journey failed: {ex.Message}");
		}
	}

	public static FollowedWalk? Load(string planId)
	{
		try
		{
			if (Read()[planId] is not JsonObject item)
			{
				return null;
			}

			return new FollowedWalk(
				item["Lead"]?.GetValue<int>() ?? 0,
				item["From"]?.GetValue<string>(),
				item["FromLat"]?.GetValue<double>(),
				item["FromLon"]?.GetValue<double>(),
				item["Trail"]?.GetValue<int>() ?? 0,
				item["To"]?.GetValue<string>(),
				item["ToLat"]?.GetValue<double>(),
				item["ToLon"]?.GetValue<double>());
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Reading the walks of a followed journey failed: {ex.Message}");

			return null;
		}
	}

	/// <summary>Drops the walks of journeys that are no longer followed.</summary>
	public static void Keep(IEnumerable<string> planIds)
	{
		try
		{
			HashSet<string> keep = [.. planIds];
			JsonObject all = Read();

			string[] stale = [.. all.Select(pair => pair.Key).Where(id => !keep.Contains(id))];

			if (stale.Length == 0)
			{
				return;
			}

			foreach (string id in stale)
			{
				all.Remove(id);
			}

			Preferences.Default.Set(Key, all.ToJsonString());
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Cleaning the walks of followed journeys failed: {ex.Message}");
		}
	}

	private static JsonObject Read()
	{
		string text = Preferences.Default.Get(Key, string.Empty);

		return text.Length > 0
			&& JsonNode.Parse(text) is JsonObject root
				? root
				: [];
	}
}
