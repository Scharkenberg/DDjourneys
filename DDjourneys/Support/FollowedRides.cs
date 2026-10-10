using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;

namespace DDjourneys.Support;

/// <summary>One ride of a followed journey, as much of it as is needed to look its vehicle up again.</summary>
public sealed record FollowedRide(
	string Line,
	TransitMode Mode,
	string? Destination,
	DateTimeOffset? Departure,
	DateTimeOffset? Arrival,
	Station From)
{
	/// <summary>A leg for <c>LegRunResolver</c> (the stop it boards at, the line and the scheduled time).</summary>
	public JourneyLeg ToLeg() =>
		new()
		{
			Mode = Mode,
			From = From,
			To = From,
			ScheduledDeparture = Departure,
			ScheduledArrival = Arrival,
			Line =
				new TransitLine
				{
					Name = Line,
					Mode = Mode,
					Destination = Destination
				}
		};
}

/// <summary>
/// The rides of the followed journeys, kept on the device: the tracking service knows stops by name, but looking the
/// vehicles up (and drawing them on a map) needs the boarding stops with their ids. Written when a journey is followed,
/// dropped when it is no longer followed. JSON by hand (no reflection).
/// </summary>
public static class FollowedRides
{
	private const string Key = "tracking.rides";

	// Parsed once per plan (the overview page asks every few seconds while a journey is under way).
	private static readonly Lock CacheGate = new();
	private static readonly Dictionary<string, IReadOnlyList<FollowedRide>> RidesByPlan = new(StringComparer.Ordinal);

	public static void Save(string planId, Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		try
		{
			JsonObject all = Read();

			var rides = new JsonArray();

			foreach (JourneyLeg leg in journey.Legs.Where(leg => leg.IsRide && leg.Line is not null))
			{
				rides.Add(
					new JsonObject
					{
						["Line"] = leg.Line!.Name,
						["Mode"] = (int)leg.Mode,
						["Destination"] = leg.Line.Destination,
						["Departure"] = Stamp(leg.ScheduledDeparture),
						["Arrival"] = Stamp(leg.ScheduledArrival),
						["StopId"] = leg.From.Id,
						["ProviderId"] = leg.From.ProviderId,
						["StopName"] = leg.From.Name,
						["StopPlace"] = leg.From.Place,
						["Latitude"] = leg.From.Latitude,
						["Longitude"] = leg.From.Longitude
					});
			}

			all[planId] = rides;

			Preferences.Default.Set(Key, all.ToJsonString());

			lock (CacheGate)
			{
				RidesByPlan.Remove(planId);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Remembering the rides of a followed journey failed: {ex.Message}");
		}
	}

	public static IReadOnlyList<FollowedRide> Load(string planId)
	{
		lock (CacheGate)
		{
			if (RidesByPlan.TryGetValue(planId, out IReadOnlyList<FollowedRide>? cached))
			{
				return cached;
			}
		}

		IReadOnlyList<FollowedRide> rides = ParseRides(planId);

		lock (CacheGate)
		{
			RidesByPlan[planId] = rides;
		}

		return rides;
	}

	private static List<FollowedRide> ParseRides(string planId)
	{
		try
		{
			if (Read()[planId] is not JsonArray rides)
			{
				return [];
			}

			var result = new List<FollowedRide>();

			foreach (JsonNode? node in rides)
			{
				if (node is not JsonObject item
					|| item["Line"]?.GetValue<string>() is not { Length: > 0 } line
					|| item["StopId"]?.GetValue<string>() is not { Length: > 0 } stopId)
				{
					continue;
				}

				result.Add(
					new FollowedRide(
						line,
						(TransitMode)(item["Mode"]?.GetValue<int>() ?? 0),
						item["Destination"]?.GetValue<string>(),
						Parse(item["Departure"]),
						Parse(item["Arrival"]),
						new Station
						{
							Id = stopId,
							ProviderId = item["ProviderId"]?.GetValue<string>() ?? string.Empty,
							Name = item["StopName"]?.GetValue<string>() ?? string.Empty,
							Place = item["StopPlace"]?.GetValue<string>(),
							Latitude = item["Latitude"]?.GetValue<double>(),
							Longitude = item["Longitude"]?.GetValue<double>()
						}));
			}

			return result;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Reading the rides of a followed journey failed: {ex.Message}");

			return [];
		}
	}

	public static bool Has(string planId) =>
		Load(planId).Count > 0;

	/// <summary>Drops the rides of journeys that are no longer followed.</summary>
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

			lock (CacheGate)
			{
				foreach (string id in stale)
				{
					RidesByPlan.Remove(id);
				}
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Cleaning the rides of followed journeys failed: {ex.Message}");
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

	private static string? Stamp(DateTimeOffset? time) =>
		time?.ToString("O", CultureInfo.InvariantCulture);

	private static DateTimeOffset? Parse(JsonNode? node) =>
		node?.GetValue<string>() is { } text
		&& DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset time)
			? time
			: null;
}
