using System.Globalization;
using System.Text.Json;
using DDjourneys.Core.Api;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Providers.OpenData;

/// <summary>
/// The Dresden open data portal (OGC API Features, GeoJSON in WGS84, no key):
/// <c>L1233</c> stops with accessibility data, <c>L1087</c> DVB service points.
/// </summary>
public sealed class DresdenOpenDataProvider : IOpenDataProvider
{
	private const string Base =
		"https://kommisdd.dresden.de/net4/public/ogcapi/collections";

	private const string AccessibilityLayer = "L1233";

	private const string ServicePointLayer = "L1087";

	private readonly ApiClient _apiClient;

	public DresdenOpenDataProvider(
		ApiClient apiClient)
	{
		ArgumentNullException.ThrowIfNull(apiClient);

		_apiClient = apiClient;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<StopAccessibility>> GetStopAccessibilityAsync(
		Location stop,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(stop);

		if (stop.Latitude is not { } latitude
			|| stop.Longitude is not { } longitude)
		{
			return [];
		}

		IReadOnlyList<Feature> features =
			await QueryAsync(
				AccessibilityLayer,
				latitude,
				longitude,
				150,
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		List<StopAccessibility> all =
			[.. features.Select(ToAccessibility)];

		// The box also catches neighbouring stops: keep the ones with the stop's own name when there are any.
		string wanted = Normalize(stop.Name);

		List<StopAccessibility> named =
			[.. all.Where(
				entry => Normalize(entry.StopName) is { Length: > 0 } name
					&& (name.Contains(wanted, StringComparison.Ordinal)
						|| wanted.Contains(name, StringComparison.Ordinal)))];

		return
			[.. (named.Count > 0 ? named : all)
				.OrderBy(entry => entry.Platform, StringComparer.CurrentCultureIgnoreCase)];
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<ServicePoint>> GetServicePointsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 3000,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<Feature> features =
			await QueryAsync(
				ServicePointLayer,
				latitude,
				longitude,
				radiusMeters,
				timeout,
				cancellationToken)
				.ConfigureAwait(false);

		return
			[.. features
				.Where(feature => feature.Latitude is not null && feature.Longitude is not null)
				.Select(
					feature =>
					{
						string name =
							FirstText(feature.Properties, "name", "bezeichnung", "titel", "standort", "einrichtung")
							?? feature.Properties.Values.FirstOrDefault(value => value.Length > 0)
							?? ServicePointLayer;

						return new ServicePoint
						{
							Name = name,
							Details =
								[.. feature.Properties
									.Where(property => property.Value.Length > 0 && property.Value != name)
									.Select(property => new KeyValuePair<string, string>(property.Key, property.Value))],
							Latitude = feature.Latitude!.Value,
							Longitude = feature.Longitude!.Value,
							DistanceMeters =
								(int)Math.Round(
									GeoMath.DistanceMeters(
										latitude,
										longitude,
										feature.Latitude!.Value,
										feature.Longitude!.Value))
						};
					})
				.Where(point => point.DistanceMeters <= radiusMeters * 1.25)
				.OrderBy(point => point.DistanceMeters)
				.Take(25)];
	}

	private static StopAccessibility ToAccessibility(Feature feature) =>
		new()
		{
			StopName = FirstText(feature.Properties, "hst_name") ?? string.Empty,
			Platform = FirstText(feature.Properties, "steig"),
			GlobalId = FirstText(feature.Properties, "globale_id"),
			Boarding = FirstText(feature.Properties, "best_einstieg"),
			KerbHeight = FirstText(feature.Properties, "bordhoehe"),
			Width = FirstText(feature.Properties, "breite"),
			TactileGuidance = FirstText(feature.Properties, "ls_txt"),
			AudioAnnouncements = FirstText(feature.Properties, "afs_txt"),
			Latitude = feature.Latitude,
			Longitude = feature.Longitude
		};

	private static string? FirstText(
		IReadOnlyDictionary<string, string> properties,
		params string[] names)
	{
		foreach (string name in names)
		{
			foreach (KeyValuePair<string, string> property in properties)
			{
				if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)
					&& property.Value.Length > 0)
				{
					return property.Value;
				}
			}
		}

		return null;
	}

	private static string Normalize(string text) =>
		string.Concat(
			text
				.Where(char.IsLetterOrDigit)
				.Select(char.ToLowerInvariant));

	/// <summary>Features inside a box of <paramref name="radiusMeters"/> around a position.</summary>
	private async Task<IReadOnlyList<Feature>> QueryAsync(
		string layer,
		double latitude,
		double longitude,
		int radiusMeters,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
	{
		double dLatitude = radiusMeters / 111_320.0;
		double dLongitude = radiusMeters / (111_320.0 * Math.Cos(latitude * Math.PI / 180));

		string uri =
			string.Create(
				CultureInfo.InvariantCulture,
				$"{Base}/{layer}/items?limit=200&bbox={longitude - dLongitude:F6},{latitude - dLatitude:F6},{longitude + dLongitude:F6},{latitude + dLatitude:F6}");

		string json =
			await _apiClient
				.GetAsync(uri, timeout, cancellationToken)
				.ConfigureAwait(false);

		return ParseFeatures(json);
	}

	/// <summary>GeoJSON FeatureCollection to features; geometry is reduced to one WGS84 point (the first vertex of lines).</summary>
	internal static IReadOnlyList<Feature> ParseFeatures(string json)
	{
		var found = new List<Feature>();

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);

			if (!document.RootElement.TryGetProperty("features", out JsonElement features)
				|| features.ValueKind != JsonValueKind.Array)
			{
				return found;
			}

			foreach (JsonElement feature in features.EnumerateArray())
			{
				var properties =
					new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

				if (feature.TryGetProperty("properties", out JsonElement props)
					&& props.ValueKind == JsonValueKind.Object)
				{
					foreach (JsonProperty property in props.EnumerateObject())
					{
						string? text =
							property.Value.ValueKind switch
							{
								JsonValueKind.String => property.Value.GetString(),
								JsonValueKind.Number => property.Value.GetRawText(),
								JsonValueKind.True => "true",
								JsonValueKind.False => "false",
								_ => null
							};

						if (!string.IsNullOrWhiteSpace(text))
						{
							properties[property.Name] = text.Trim();
						}
					}
				}

				(double Latitude, double Longitude)? point =
					feature.TryGetProperty("geometry", out JsonElement geometry)
					&& geometry.ValueKind == JsonValueKind.Object
					&& geometry.TryGetProperty("coordinates", out JsonElement coordinates)
						? FirstPosition(coordinates)
						: null;

				found.Add(
					new Feature(
						properties,
						point?.Latitude,
						point?.Longitude));
			}
		}
		catch (JsonException)
		{
			// Not a feature collection: nothing to show.
		}

		return found;
	}

	/// <summary>[lon, lat] of a point, or of the first vertex of a (multi)line / polygon.</summary>
	private static (double Latitude, double Longitude)? FirstPosition(JsonElement coordinates)
	{
		if (coordinates.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		if (coordinates.GetArrayLength() >= 2
			&& coordinates[0].ValueKind == JsonValueKind.Number
			&& coordinates[1].ValueKind == JsonValueKind.Number)
		{
			return (coordinates[1].GetDouble(), coordinates[0].GetDouble());
		}

		foreach (JsonElement child in coordinates.EnumerateArray())
		{
			if (FirstPosition(child) is { } position)
			{
				return position;
			}
		}

		return null;
	}

	internal sealed record Feature(
		IReadOnlyDictionary<string, string> Properties,
		double? Latitude,
		double? Longitude);
}
