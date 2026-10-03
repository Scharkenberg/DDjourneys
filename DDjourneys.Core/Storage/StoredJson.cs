using System.Text.Json;

namespace DDjourneys.Core.Storage;

/// <summary>What could be read from a stored list.</summary>
/// <param name="Items">Every entry that could be understood.</param>
/// <param name="Dropped">Entries that could not be understood and were skipped.</param>
/// <param name="Recognized">False when the text is neither a list nor a versioned list (corrupt or foreign).</param>
/// <param name="Version">Schema version of the list (1 = the bare array of the first releases).</param>
public sealed record StoredRead<T>(
	IReadOnlyList<T> Items,
	int Dropped,
	bool Recognized,
	int Version);

/// <summary>
/// Small lists kept as JSON text. The text is a versioned envelope <c>{"v":2,"items":[...]}</c>; the bare array
/// of the first releases is still read. Reading is entry by entry: one entry that does not fit (a changed
/// type, a field that is gone) costs that entry, never the whole list.
/// </summary>
public static class StoredJson
{
	public const int CurrentVersion = 2;

	private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

	public static StoredRead<T> Read<T>(string? json, Func<JsonElement, T?> parse)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(parse);

		if (string.IsNullOrWhiteSpace(json))
		{
			return new StoredRead<T>([], 0, true, CurrentVersion);
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);

			JsonElement root = document.RootElement;
			int version = 1;
			JsonElement items;

			if (root.ValueKind == JsonValueKind.Array)
			{
				items = root;
			}
			else if (root.ValueKind == JsonValueKind.Object
				&& root.TryGetProperty("items", out items)
				&& items.ValueKind == JsonValueKind.Array)
			{
				version =
					root.TryGetProperty("v", out JsonElement v) && v.TryGetInt32(out int number)
						? number
						: CurrentVersion;
			}
			else
			{
				return new StoredRead<T>([], 0, false, 0);
			}

			var result = new List<T>(items.GetArrayLength());
			int dropped = 0;

			foreach (JsonElement element in items.EnumerateArray())
			{
				try
				{
					if (parse(element) is { } item)
					{
						result.Add(item);
						continue;
					}
				}
				catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
				{
				}

				dropped++;
			}

			return new StoredRead<T>(result, dropped, true, version);
		}
		catch (JsonException)
		{
			return new StoredRead<T>([], 0, false, 0);
		}
	}

	public static string Write<T>(IEnumerable<T> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		return JsonSerializer.Serialize(new Envelope<T> { V = CurrentVersion, Items = items.ToArray() }, Options);
	}

	// ----- tolerant field readers -----

	public static string? String(JsonElement element, string name)
	{
		if (!TryGet(element, name, out JsonElement value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.GetRawText(),
			JsonValueKind.True => bool.TrueString,
			JsonValueKind.False => bool.FalseString,
			_ => null
		};
	}

	public static double? Number(JsonElement element, string name)
	{
		if (!TryGet(element, name, out JsonElement value))
		{
			return null;
		}

		if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
		{
			return double.IsFinite(number) ? number : null;
		}

		return value.ValueKind == JsonValueKind.String
			&& double.TryParse(
				value.GetString(),
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out double parsed)
			&& double.IsFinite(parsed)
				? parsed
				: null;
	}

	public static bool TryGet(JsonElement element, string name, out JsonElement value)
	{
		value = default;

		if (element.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;

				return value.ValueKind != JsonValueKind.Null;
			}
		}

		return false;
	}

	private sealed class Envelope<T>
	{
		[System.Text.Json.Serialization.JsonPropertyName("v")]
		public int V { get; init; }

		[System.Text.Json.Serialization.JsonPropertyName("items")]
		public T[] Items { get; init; } = [];
	}
}
