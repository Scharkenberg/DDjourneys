using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DDjourneys.Core.Api;
using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Core.Services;

/// <summary>
/// One bike track of the BRouter service: the length and the riding time it quotes, and the hills of the
/// track. The ascent is BRouter's own "filtered ascend" (the figure without the noise of small bumps);
/// the descent is derived from it, see <see cref="BrouterClient.Parse"/>.
/// </summary>
public sealed record BikeRoute(
	int DistanceMeters,
	TimeSpan Duration,
	int AscentMeters,
	int DescentMeters,
	IReadOnlyList<(double Latitude, double Longitude)> Path);

/// <summary>
/// The public BRouter web service (brouter.de) that plans bike tracks between two points. No key, no
/// authentication; one GET per track. A track that cannot be asked or read answers null — the feeder above
/// this client decides what that means for the journey. Only tracks that were read are cached (the
/// <see cref="TtlCache"/> contract: answers, never failures).
/// </summary>
public sealed class BrouterClient(ApiClient apiClient)
{
	/// <summary>The budget of one track request: the host answers fast; this guards the search against a silent one.</summary>
	public const int TimeoutSeconds = 10;

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

	// A bike track changes with the OSM data under it, not while somebody plans a journey; paging a
	// result list asks the same tracks again within minutes.
	private readonly TtlCache<BikeRoute> _routes = new(TimeSpan.FromMinutes(10));

	/// <summary>
	/// The query string of one track request: the points as <c>lon,lat</c> (longitude first, the service's
	/// convention), pipe-separated, six decimals, with the fixed parameters of the app (the trekking
	/// profile, the first alternative, GeoJSON).
	/// </summary>
	public static string Query(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude) =>
		$"lonlats={Point(fromLongitude, fromLatitude)}|{Point(toLongitude, toLatitude)}&profile=trekking&alternativeidx=0&format=geojson";

	/// <summary>
	/// Reads one GeoJSON track answer. The properties carry their numbers as strings: "track-length" (metres),
	/// "filtered ascend" (metres, the ascent) and "total-time" (seconds); the coordinates are
	/// [lon, lat, elevation] triples. The descent is derived, in one fixed way: the filtered ascent minus the
	/// height difference of the track's ends (ascent − descent = end − start, so the two figures stay
	/// consistent with the track), never below zero (rounding in the filtered figure may tip a flat track).
	/// Null when the answer does not carry all of that.
	/// </summary>
	public static BikeRoute? Parse(string json)
	{
		try
		{
			JsonNode? root = JsonNode.Parse(json);

			// An empty features array would throw on [0] (the node indexer does not bounds-check).
			if (root?["features"] as JsonArray is not { Count: > 0 } features
				|| features[0] is not { } feature
				|| feature["properties"] is not JsonObject properties
				|| feature["geometry"]?["coordinates"] is not JsonArray { Count: >= 2 } coordinates)
			{
				return null;
			}

			if (!Number(properties, "track-length", out int meters)
				|| !Number(properties, "total-time", out int seconds)
				|| !Number(properties, "filtered ascend", out int ascent))
			{
				return null;
			}

			var path = new List<(double Latitude, double Longitude)>(coordinates.Count);
			double? firstElevation = null;
			double? lastElevation = null;

			foreach (JsonNode? point in coordinates)
			{
				if (point is not JsonArray { Count: >= 2 } triple
					|| !Value(triple[0], out double longitude)
					|| !Value(triple[1], out double latitude))
				{
					return null;
				}

				path.Add((latitude, longitude));

				// The hills are read from the ends of the track itself, not from the first point that
				// happens to carry a height.
				if (path.Count == 1)
				{
					firstElevation = Elevation(triple);
				}

				lastElevation = Elevation(triple);
			}

			// The hills need the height of both ends; a track without them cannot say what it climbs.
			if (firstElevation is not { } from || lastElevation is not { } to)
			{
				return null;
			}

			int descent = Math.Max(0, ascent - (int)Math.Round(to - from));

			return new BikeRoute(
				meters,
				TimeSpan.FromSeconds(seconds),
				ascent,
				descent,
				path);
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
		{
			return null;
		}
	}

	/// <summary>
	/// One bike track between two points, cached per pair while the cache holds. Null when the service does not
	/// answer or its answer cannot be read (both are logged); failures are never cached.
	/// </summary>
	public async Task<BikeRoute?> RouteAsync(
		double fromLatitude,
		double fromLongitude,
		double toLatitude,
		double toLongitude,
		CancellationToken cancellationToken = default)
	{
		string key = $"track|{Point(fromLatitude, fromLongitude)}|{Point(toLatitude, toLongitude)}";

		if (_routes.TryGet(key, out BikeRoute? cached))
		{
			return cached;
		}

		string url = $"{InterfaceSchemas.BrouterUrl}?{Query(fromLatitude, fromLongitude, toLatitude, toLongitude)}";

		try
		{
			string json = await apiClient.GetAsync(url, Timeout, cancellationToken).ConfigureAwait(false);

			BikeRoute? route = Parse(json);

			if (route is null)
			{
				DiagnosticLog.Write($"[Bike] brouter answered a track that cannot be read ({json.Length} characters)");
			}
			else
			{
				_routes.Set(key, route);

				DiagnosticLog.Write(
					$"[Bike] brouter track {route.DistanceMeters} m, {route.Duration.TotalMinutes:0.#} min, +{route.AscentMeters} m up, from {fromLatitude:F5},{fromLongitude:F5} to {toLatitude:F5},{toLongitude:F5}");
			}

			return route;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Bike] brouter failed: {ex.GetType().Name}: {ex.Message}");

			return null;
		}
	}

	private static string Point(double first, double second) =>
		$"{Math.Round(first, 6).ToString(CultureInfo.InvariantCulture)},{Math.Round(second, 6).ToString(CultureInfo.InvariantCulture)}";

	private static double? Elevation(JsonArray triple) =>
		triple.Count >= 3 && Value(triple[2], out double elevation)
			? elevation
			: null;

	/// <summary>A property value that comes as a string ("1102") or, defensively, as a number.</summary>
	private static bool Number(JsonObject properties, string name, out int value)
	{
		value = 0;

		return Value(properties[name], out double parsed)
			&& parsed is >= 0 and < int.MaxValue
			&& int.TryParse(
				Math.Round(parsed).ToString(CultureInfo.InvariantCulture),
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out value);
	}

	/// <summary>A JSON value that is a number, or a string that names one ("1102").</summary>
	private static bool Value(JsonNode? node, out double value)
	{
		value = 0;

		if (node is null)
		{
			return false;
		}

		if (node.GetValueKind() == JsonValueKind.Number)
		{
			value = node.GetValue<double>();

			return double.IsFinite(value);
		}

		return node.GetValueKind() == JsonValueKind.String
			&& double.TryParse(
				node.GetValue<string>(),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out value)
			&& double.IsFinite(value);
	}
}
