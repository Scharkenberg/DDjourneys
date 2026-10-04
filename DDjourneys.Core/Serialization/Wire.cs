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

	/// <summary>{"name": value}</summary>
	public static string Single(string name, string value) =>
		new JsonObject { [name] = value }.ToJsonString();
}
