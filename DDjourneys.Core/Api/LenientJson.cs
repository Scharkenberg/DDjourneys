using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDjourneys.Core.Api;

/// <summary>
/// A text field that some publishers write as a number ("ppid": 3663 where "3663" was assumed): any scalar is read
/// as its text, objects and arrays as nothing. A field typed too strictly made the whole document unreadable before
/// (the P+R file failed on its first site).
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string?>
{
	public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.String:
				return reader.GetString();
			case JsonTokenType.Number:
				return reader.TryGetInt64(out long whole)
					? whole.ToString(CultureInfo.InvariantCulture)
					: reader.GetDouble().ToString("R", CultureInfo.InvariantCulture);
			case JsonTokenType.True:
				return "true";
			case JsonTokenType.False:
				return "false";
			case JsonTokenType.StartObject:
			case JsonTokenType.StartArray:
				reader.Skip();
				return null;
			default:
				return null;
		}
	}

	public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStringValue(value);
		}
	}
}

/// <summary>
/// A yes/no field: JSON booleans (GBFS 2.1 and later: "is_renting": true), the integers of GBFS 1.x/2.0 (1, 0) and
/// their texts ("true", "1"). Anything else is unknown. Typed as int before, the boolean feeds failed whole and every
/// station showed zero bikes.
/// </summary>
public sealed class LenientFlagConverter : JsonConverter<bool?>
{
	public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.True:
				return true;
			case JsonTokenType.False:
				return false;
			case JsonTokenType.Number:
				return reader.TryGetDouble(out double number) ? number != 0 : null;
			case JsonTokenType.String:
				return reader.GetString()?.Trim().ToLowerInvariant() switch
				{
					"true" or "1" or "yes" => true,
					"false" or "0" or "no" => false,
					_ => null
				};
			case JsonTokenType.StartObject:
			case JsonTokenType.StartArray:
				reader.Skip();
				return null;
			default:
				return null;
		}
	}

	public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
	{
		if (value is { } flag)
		{
			writer.WriteBooleanValue(flag);
		}
		else
		{
			writer.WriteNullValue();
		}
	}
}

/// <summary>A count or a measure that may come as an integer, a decimal or a text ("current_range_meters": 55200.0).</summary>
public sealed class LenientNumberConverter : JsonConverter<double?>
{
	public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		switch (reader.TokenType)
		{
			case JsonTokenType.Number:
				return reader.TryGetDouble(out double number) ? number : null;
			case JsonTokenType.String:
				return double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
			case JsonTokenType.StartObject:
			case JsonTokenType.StartArray:
				reader.Skip();
				return null;
			default:
				return null;
		}
	}

	public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
	{
		if (value is { } number)
		{
			writer.WriteNumberValue(number);
		}
		else
		{
			writer.WriteNullValue();
		}
	}
}
