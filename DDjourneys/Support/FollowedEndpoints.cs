using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;

namespace DDjourneys.Support;

/// <summary>
/// The ends of the followed journeys, kept on the device (key <c>tracking.endpoints</c>): the recovery search of
/// a missed connection replans from them. Written when a journey is followed, dropped when it is forgotten.
/// The list itself is <see cref="FollowedEndpointList"/> (pure, unit-tested); this is only the storage access.
/// </summary>
public static class FollowedEndpoints
{
	private const string Key = "tracking.endpoints";

	public static void Save(string planId, Location? from, Location? to, Location? via)
	{
		if (from is null && to is null && via is null)
		{
			return;
		}

		try
		{
			IReadOnlyList<FollowedEndpoint> all = FollowedEndpointList.Without(Load(), planId);

			Preferences.Default.Set(
				Key,
				FollowedEndpointList.Write(
					(List<FollowedEndpoint>)[.. all, new FollowedEndpoint(planId, from, to, via)]));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Remembering the ends of a followed journey failed: {ex.Message}");
		}
	}

	public static FollowedEndpoint? Find(string planId) =>
		Load().FirstOrDefault(endpoint => string.Equals(endpoint.PlanId, planId, StringComparison.Ordinal));

	/// <summary>Drops the entry of a journey that is no longer followed.</summary>
	public static void Forget(string planId)
	{
		try
		{
			IReadOnlyList<FollowedEndpoint> all = Load();

			if (all.All(endpoint => !string.Equals(endpoint.PlanId, planId, StringComparison.Ordinal)))
			{
				return;
			}

			Preferences.Default.Set(Key, FollowedEndpointList.Write((List<FollowedEndpoint>)[.. FollowedEndpointList.Without(all, planId)]));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Dropping the ends of a followed journey failed: {ex.Message}");
		}
	}

	private static IReadOnlyList<FollowedEndpoint> Load()
	{
		try
		{
			return FollowedEndpointList.Parse(Preferences.Default.Get(Key, string.Empty));
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Reading the ends of followed journeys failed: {ex.Message}");

			return [];
		}
	}
}
