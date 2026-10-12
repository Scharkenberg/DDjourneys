using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Storage;

/// <summary>The ends of one followed journey: where it started, where it was going, and its stop-over.</summary>
public sealed record FollowedEndpoint(
	string PlanId,
	Location? From,
	Location? To,
	Location? Via = null);

/// <summary>
/// The endpoints of followed journeys as a stored list (versioned envelope, one entry per plan id): what a
/// recovery search needs after a missed connection. Reading is entry by entry like every stored list, so one
/// entry that does not fit costs that entry, never the whole list.
/// </summary>
public static class FollowedEndpointList
{
	public static IReadOnlyList<FollowedEndpoint> Parse(string? json)
	{
		StoredRead<FollowedEndpoint> read = StoredJson.Read(json, ReadEntry);

		return read.Items;
	}

	public static string Write(IReadOnlyList<FollowedEndpoint> endpoints) =>
		StoredJson.Write(endpoints, ToNode);

	/// <summary>The list without the entry of this plan (the same list when it had none).</summary>
	public static IReadOnlyList<FollowedEndpoint> Without(
		IReadOnlyList<FollowedEndpoint> endpoints,
		string planId) =>
		[.. endpoints.Where(endpoint => !string.Equals(endpoint.PlanId, planId, StringComparison.Ordinal))];

	private static JsonNode? ToNode(FollowedEndpoint endpoint)
	{
		var node =
			new JsonObject
			{
				["PlanId"] = endpoint.PlanId
			};

		if (endpoint.From is { } from)
		{
			node["From"] = LocationJson.ToNode(from);
		}

		if (endpoint.To is { } to)
		{
			node["To"] = LocationJson.ToNode(to);
		}

		if (endpoint.Via is { } via)
		{
			node["Via"] = LocationJson.ToNode(via);
		}

		return node;
	}

	private static FollowedEndpoint? ReadEntry(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| StoredJson.String(element, "PlanId") is not { Length: > 0 } planId)
		{
			return null;
		}

		Location? from = LocationJson.FromElement(Property(element, "From"));
		Location? to = LocationJson.FromElement(Property(element, "To"));
		Location? via = LocationJson.FromElement(Property(element, "Via"));

		// An entry without any end says nothing (the writer never writes one; a foreign or older one is skipped).
		return from is null && to is null && via is null
				? null
				: new FollowedEndpoint(planId, from, to, via);
	}

	private static JsonElement Property(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement property)
			? property
			: default;
}
