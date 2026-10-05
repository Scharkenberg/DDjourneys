using System.Text.Json.Nodes;

namespace DDjourneys.Core.Serialization;

/// <summary>
/// Reflection-free JSON building (trim and AOT safe): wire objects are <see cref="JsonObject"/> trees
/// instead of anonymous types, whose members the trimmer removes.
/// </summary>
public static class Wire
{
	public static JsonArray Array(IEnumerable<JsonNode?> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		var result = new JsonArray();

		foreach (JsonNode? item in items)
		{
			result.Add(item);
		}

		return result;
	}

	public static JsonArray Array(params JsonNode?[] items) =>
		Array((IEnumerable<JsonNode?>)items);

	/// <summary>Removes the named members when they are null (an absent member, not a JSON null).</summary>
	public static JsonObject WithoutNulls(this JsonObject node, params string[] names)
	{
		ArgumentNullException.ThrowIfNull(node);

		foreach (string name in names)
		{
			if (node.TryGetPropertyValue(name, out JsonNode? value) && value is null)
			{
				node.Remove(name);
			}
		}

		return node;
	}

	/// <summary>{"name": value}</summary>
	public static string Single(string name, string value) =>
		new JsonObject { [name] = value }.ToJsonString();
}
