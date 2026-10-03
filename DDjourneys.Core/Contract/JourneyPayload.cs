using System.Globalization;
using System.Text;
using System.Text.Json;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Contract;

/// <summary>
/// The journey as other apps receive it from a <c>pick</c>: flat values for callers that only want the
/// headline, and one JSON document (schema 1) with every leg. Times are ISO 8601 with the provider
/// offset; ids are provider-qualified stop keys; mode names are the lower-case snake-case names of
/// <see cref="TransitMode"/>. Hand-written JSON keeps the schema independent of property renames.
/// </summary>
public static class JourneyPayload
{
	public const int SchemaVersion = 1;

	private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

	/// <summary>Headline values: from, to, dep, arr, duration (minutes), transfers, lines, cancelled.</summary>
	public static IReadOnlyList<KeyValuePair<string, string>> Flat(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		Station start = journey.Origin ?? journey.From;
		Station end = journey.Destination ?? journey.To;

		var values = new List<KeyValuePair<string, string>>
		{
			new("from", start.Name),
			new("to", end.Name)
		};

		AddStop(values, "from", start);
		AddStop(values, "to", end);

		if (journey.Departure is { } departure)
		{
			values.Add(new("dep", Time(departure)));
		}

		if (journey.Arrival is { } arrival)
		{
			values.Add(new("arr", Time(arrival)));
		}

		values.Add(new("duration", ((int)Math.Round(journey.Duration.TotalMinutes)).ToString(CultureInfo.InvariantCulture)));
		values.Add(new("transfers", journey.TransferCount.ToString(CultureInfo.InvariantCulture)));

		string lines =
			string.Join(
				',',
				journey.Legs
					.Where(leg => leg.IsRide)
					.Select(leg => leg.Line?.Name)
					.Where(name => !string.IsNullOrWhiteSpace(name))
					.Select(name => name!.Replace(',', ' ')));

		if (lines.Length > 0)
		{
			values.Add(new("lines", lines));
		}

		values.Add(new("cancelled", journey.IsCancelled ? "1" : "0"));

		return values;
	}

	/// <summary>The complete journey as a JSON document (schema 1).</summary>
	public static string Json(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
		{
			writer.WriteStartObject();
			writer.WriteNumber("schema", SchemaVersion);

			WriteStation(writer, "from", journey.Origin ?? journey.From);
			WriteStation(writer, "to", journey.Destination ?? journey.To);

			WriteTime(writer, "dep", journey.Departure);
			WriteTime(writer, "arr", journey.Arrival);
			writer.WriteNumber("durationMinutes", (int)Math.Round(journey.Duration.TotalMinutes));
			writer.WriteNumber("transfers", journey.TransferCount);
			writer.WriteBoolean("cancelled", journey.IsCancelled);

			writer.WriteStartArray("legs");

			foreach (JourneyLeg leg in journey.Legs)
			{
				writer.WriteStartObject();
				writer.WriteString("mode", ModeName(leg.Mode));

				if (leg.Line?.Name is { Length: > 0 } line)
				{
					writer.WriteString("line", line);
				}

				if (leg.Line?.Destination is { Length: > 0 } direction)
				{
					writer.WriteString("direction", direction);
				}

				WriteStation(writer, "from", leg.From);
				WriteTime(writer, "dep", leg.ScheduledDeparture ?? leg.EffectiveDeparture);
				WriteTime(writer, "depLive", Differs(leg.RealtimeDeparture, leg.ScheduledDeparture));
				WriteText(writer, "depPlatform", leg.DeparturePlatform);

				WriteStation(writer, "to", leg.To);
				WriteTime(writer, "arr", leg.ScheduledArrival ?? leg.EffectiveArrival);
				WriteTime(writer, "arrLive", Differs(leg.RealtimeArrival, leg.ScheduledArrival));
				WriteText(writer, "arrPlatform", leg.ArrivalPlatform);

				writer.WriteNumber("intermediateStops", Math.Max(0, leg.Stops.Count - 2));

				if (leg.IsCancelled)
				{
					writer.WriteBoolean("cancelled", true);
				}

				writer.WriteEndObject();
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	/// <summary>Lower-case snake-case name of a mode ("suburban_rail").</summary>
	public static string ModeName(TransitMode mode)
	{
		string name = mode.ToString();
		var builder = new StringBuilder(name.Length + 4);

		for (int index = 0; index < name.Length; index++)
		{
			if (char.IsUpper(name[index]) && index > 0)
			{
				builder.Append('_');
			}

			builder.Append(char.ToLowerInvariant(name[index]));
		}

		return builder.ToString();
	}

	public static string Time(DateTimeOffset value) =>
		value.ToString(TimeFormat, CultureInfo.InvariantCulture);

	private static void AddStop(List<KeyValuePair<string, string>> values, string prefix, Station station)
	{
		if (station.StopKey is { } key)
		{
			values.Add(new($"{prefix}.stop", key));
		}

		if (station is { Latitude: { } lat, Longitude: { } lon })
		{
			values.Add(new($"{prefix}.lat", lat.ToString("0.######", CultureInfo.InvariantCulture)));
			values.Add(new($"{prefix}.lon", lon.ToString("0.######", CultureInfo.InvariantCulture)));
		}
	}

	private static void WriteStation(Utf8JsonWriter writer, string name, Station station)
	{
		writer.WriteStartObject(name);
		writer.WriteString("name", station.Name);
		WriteText(writer, "place", station.Place);
		WriteText(writer, "stop", station.StopKey);

		if (station is { Latitude: { } lat, Longitude: { } lon })
		{
			writer.WriteNumber("lat", Math.Round(lat, 6));
			writer.WriteNumber("lon", Math.Round(lon, 6));
		}

		writer.WriteEndObject();
	}

	private static void WriteTime(Utf8JsonWriter writer, string name, DateTimeOffset? value)
	{
		if (value is { } time)
		{
			writer.WriteString(name, Time(time));
		}
	}

	private static void WriteText(Utf8JsonWriter writer, string name, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			writer.WriteString(name, value.Trim());
		}
	}

	/// <summary>The real-time value when it differs from the plan by a minute or more.</summary>
	private static DateTimeOffset? Differs(DateTimeOffset? realtime, DateTimeOffset? planned) =>
		realtime is { } live
		&& planned is { } plan
		&& Math.Abs((live - plan).TotalMinutes) >= 1
			? live
			: null;
}
