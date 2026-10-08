using System.Text;
using System.Text.Json;

namespace DDjourneys.Core.Mapping;

/// <summary>What a marker looks like on the map.</summary>
public enum MapMarkerKind
{
	/// <summary>A stop: small ring in the colour of its line.</summary>
	Stop,

	/// <summary>Where the journey starts.</summary>
	Start,

	/// <summary>Where the journey ends.</summary>
	End,

	/// <summary>A live vehicle: a pill carrying the line number.</summary>
	Vehicle,

	/// <summary>The stop that is meant (selected stop, position of the vehicle on its run).</summary>
	Current,

	/// <summary>The passenger.</summary>
	Me,

	/// <summary>A service point or another place of interest.</summary>
	Poi,

	/// <summary>An intermediate stop: a small, quiet dot without a name; the popup tells it.</summary>
	Knot
}


/// <summary>One marker. <see cref="Id"/> is stable so a moving marker is updated, not recreated.</summary>
public sealed record MapMarker(
	string Id,
	double Latitude,
	double Longitude,
	string Label,
	MapMarkerKind Kind = MapMarkerKind.Stop,
	string? Color = null,
	string? Title = null,
	string? Subtitle = null);


/// <summary>A path on the map.</summary>
public sealed record MapLine(
	IReadOnlyList<(double Latitude, double Longitude)> Points,
	string Color,
	bool Dashed = false,
	int Weight = 5,
	double Opacity = 1);


/// <summary>
/// Everything one map shows. The map itself (MapLibre GL in a HybridWebView) is told about it as JSON; this type
/// knows nothing about the UI and is the only contract between the app and the map page.
/// </summary>
public sealed class MapScene
{
	public static MapScene Empty { get; } = new();

	public IReadOnlyList<MapMarker> Markers { get; init; } = [];

	public IReadOnlyList<MapLine> Lines { get; init; } = [];

	/// <summary>Zoom to everything after applying; false keeps the view the user has chosen.</summary>
	public bool Fit { get; init; } = true;

	public bool IsEmpty =>
		Markers.Count == 0
		&& Lines.Count == 0;

	private static string KindName(MapMarkerKind kind) =>
		kind switch
		{
			MapMarkerKind.Start => "start",
			MapMarkerKind.End => "end",
			MapMarkerKind.Vehicle => "vehicle",
			MapMarkerKind.Current => "current",
			MapMarkerKind.Me => "me",
			MapMarkerKind.Poi => "poi",
			MapMarkerKind.Knot => "knot",
			_ => "stop"
		};

	private static bool IsValid(double latitude, double longitude) =>
		double.IsFinite(latitude)
		&& double.IsFinite(longitude)
		&& latitude is >= -90 and <= 90
		&& longitude is >= -180 and <= 180
		&& !(latitude == 0 && longitude == 0);

	/// <summary>
	/// The scene as the JSON the map page expects (<c>ddMap.set(...)</c>). Invalid coordinates are dropped.
	/// <paramref name="color"/> turns a stored colour into the one to draw (the app stores theme references, resolved
	/// at send time, so a scene follows theme changes); <paramref name="fit"/> overrides <see cref="Fit"/> (a re-send
	/// after a theme change must not move the view).
	/// </summary>
	public string ToJson(bool dark, Func<string, string>? color = null, bool? fit = null)
	{
		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream))
		{
			writer.WriteStartObject();
			writer.WriteBoolean("dark", dark);
			writer.WriteBoolean("fit", fit ?? Fit);

			writer.WriteStartArray("markers");

			foreach (MapMarker marker in Markers.Where(marker => IsValid(marker.Latitude, marker.Longitude)))
			{
				writer.WriteStartObject();
				writer.WriteString("id", marker.Id);
				writer.WriteNumber("lat", marker.Latitude);
				writer.WriteNumber("lon", marker.Longitude);
				writer.WriteString("label", marker.Label);
				writer.WriteString("kind", KindName(marker.Kind));

				if (marker.Color is not null)
				{
					writer.WriteString("color", color is null ? marker.Color : color(marker.Color));
				}

				if (marker.Title is not null)
				{
					writer.WriteString("title", marker.Title);
				}

				if (marker.Subtitle is not null)
				{
					writer.WriteString("subtitle", marker.Subtitle);
				}

				writer.WriteEndObject();
			}

			writer.WriteEndArray();

			writer.WriteStartArray("lines");

			foreach (MapLine line in Lines)
			{
				var points =
					line.Points
						.Where(point => IsValid(point.Latitude, point.Longitude))
						.ToArray();

				if (points.Length < 2)
				{
					continue;
				}

				writer.WriteStartObject();
				writer.WriteString("color", color is null ? line.Color : color(line.Color));
				writer.WriteBoolean("dashed", line.Dashed);
				writer.WriteNumber("weight", line.Weight);
				writer.WriteNumber("opacity", Math.Clamp(line.Opacity, 0.05, 1));
				writer.WriteStartArray("points");

				foreach ((double latitude, double longitude) in points)
				{
					writer.WriteStartArray();
					writer.WriteNumberValue(latitude);
					writer.WriteNumberValue(longitude);
					writer.WriteEndArray();
				}

				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}
}
