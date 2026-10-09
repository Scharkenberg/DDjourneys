using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Serialization;

/// <summary>
/// The map data of an answer: the documented wire form is an array of pipe-delimited coordinate lists. A bare
/// string is taken as one list, so a deviation in one field never fails the whole response.
/// </summary>
public sealed class VvoMapDataConverter
	: JsonConverter<IReadOnlyList<string>>
{
	public override IReadOnlyList<string> Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.Null:
				return [];

			case JsonTokenType.String:
				return [reader.GetString() ?? string.Empty];

			case JsonTokenType.StartArray:
			{
				List<string> values = [];

				while (reader.Read())
				{
					if (reader.TokenType == JsonTokenType.EndArray)
					{
						return values;
					}

					if (reader.TokenType != JsonTokenType.String)
					{
						throw new JsonException("Expected a string of map data.");
					}

					values.Add(reader.GetString() ?? string.Empty);
				}

				break;
			}

			default:
				break;
		}

		throw new JsonException("Expected an array or a string of map data.");
	}

	public override void Write(
		Utf8JsonWriter writer,
		IReadOnlyList<string> value,
		JsonSerializerOptions options)
	{
		writer.WriteStartArray();

		foreach (string entry in value)
		{
			writer.WriteStringValue(entry);
		}

		writer.WriteEndArray();
	}
}
