using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Storage;

/// <summary>A place as stored JSON (settings, widgets): built and read without reflection.</summary>
public static class LocationJson
{
	public static JsonNode? ToNode(Location? place) =>
		place is null
			? null
			: new JsonObject
			{
				["Id"] = place.Id,
				["Name"] = place.Name,
				["Place"] = place.Place,
				["Latitude"] = place.Latitude,
				["Longitude"] = place.Longitude,
				["ProviderId"] = place.ProviderId,
				["Kind"] = place.Kind.ToString()
			};

	public static string ToJson(Location place)
	{
		ArgumentNullException.ThrowIfNull(place);

		return ToNode(place)!.ToJsonString();
	}

	/// <summary>The place in <paramref name="element"/>; null when it is not one (no name).</summary>
	public static Location? FromElement(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object
			|| StoredJson.String(element, "Name") is not { Length: > 0 } name)
		{
			return null;
		}

		return new Location
		{
			Id = StoredJson.String(element, "Id"),
			Name = name,
			Place = StoredJson.String(element, "Place"),
			Latitude = StoredJson.Number(element, "Latitude"),
			Longitude = StoredJson.Number(element, "Longitude"),
			ProviderId = StoredJson.String(element, "ProviderId") ?? string.Empty,
			Kind = Enum.TryParse(StoredJson.String(element, "Kind"), out PlaceKind kind) ? kind : PlaceKind.Stop
		};
	}

	public static Location? FromJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);

			return FromElement(document.RootElement);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
