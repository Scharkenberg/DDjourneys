using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDjourneys.Core.Providers.Vvo.Serialization;

/// <summary>
/// Converts Microsoft JSON dates used by the VVO WebAPI
/// into DateTimeOffset values.
/// 
/// Example:
/// /Date(1512770460000+0100)/
/// </summary>
public sealed class VvoDateTimeOffsetConverter
	: JsonConverter<DateTimeOffset?>
{
	private const string Prefix = "/Date(";


	public override DateTimeOffset? Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
		{
			return null;
		}


		if (reader.TokenType != JsonTokenType.String)
		{
			throw new JsonException(
				"Expected a JSON string containing a VVO date.");
		}


		string? value = reader.GetString();


		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}


		if (!value.StartsWith(Prefix, StringComparison.Ordinal) ||
			!value.EndsWith(")/", StringComparison.Ordinal))
		{
			throw new JsonException(
				$"Invalid VVO date format: {value}");
		}


		string inner =
			value[6..^2];


		int timezoneIndex =
			inner.IndexOfAny(['+', '-']);


		string millisecondsPart =
			timezoneIndex >= 0
				? inner[..timezoneIndex]
				: inner;


		if (!long.TryParse(
				millisecondsPart,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out long milliseconds))
		{
			throw new JsonException(
				$"Invalid VVO timestamp: {value}");
		}


		DateTimeOffset result =
			DateTimeOffset.FromUnixTimeMilliseconds(
				milliseconds);


		return result;
	}


	public override void Write(
		Utf8JsonWriter writer,
		DateTimeOffset? value,
		JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}


		long milliseconds =
			value.Value.ToUnixTimeMilliseconds();


		writer.WriteStringValue(
			$"/Date({milliseconds}+0000)/");
	}
}