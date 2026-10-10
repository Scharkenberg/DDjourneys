using System.Text.Json;
using DDjourneys.Core.Mapping;
using DDjourneys.Core.Models;

namespace DDjourneys.Tracking.Tests;

/// <summary>Outlines made small enough to send (Douglas-Peucker in metres) and the polygon node of the map payload.</summary>
public sealed class PolylineAndSceneTests
{
	private static readonly (double Latitude, double Longitude) Square = (51.05, 13.73);

	[Fact]
	public void Collinear_points_collapse_to_their_ends()
	{
		var line = new List<(double Latitude, double Longitude)>
			{
				(51.0000, 13.0000),
				(51.0001, 13.0200),
				(51.0002, 13.0400),
				(51.0003, 13.0600),
				(51.0004, 13.0800)
			};

		IReadOnlyList<(double Latitude, double Longitude)> simplified = PolylineSimplify.Simplify(line, 50);

		Assert.Equal(2, simplified.Count);
		Assert.Equal(line[0], simplified[0]);
		Assert.Equal(line[^1], simplified[^1]);
	}

	[Fact]
	public void A_detour_survives_while_the_tolerance_allows_it()
	{
		// About 550 m north of the line through the ends: too far to drop at 300 m, gone at 1 km.
		var line = new List<(double Latitude, double Longitude)>
			{
				(51.0000, 13.0000),
				(51.0050, 13.0400),
				(51.0000, 13.0800)
			};

		Assert.Equal(3, PolylineSimplify.Simplify(line, 300).Count);
		Assert.Equal(2, PolylineSimplify.Simplify(line, 1000).Count);
	}

	[Fact]
	public void A_closed_ring_loses_its_duplicate_anchor_and_gets_it_back()
	{
		// The closing point would break the recursion (the segment endpoints coincide); SimplifyRing strips it.
		var ring = new List<(double Latitude, double Longitude)>
			{
				(51.0000, 13.0000),
				(51.0050, 13.0000),
				(51.0050, 13.0050),
				(51.0000, 13.0050),
				(51.0000, 13.0000)
			};

		IReadOnlyList<(double Latitude, double Longitude)> simplified = PolylineSimplify.SimplifyRing(ring, 4, 5);

		Assert.Equal(simplified[0], simplified[^1]);
		Assert.Equal(ring[0], simplified[0]);
		Assert.Contains((51.0050, 13.0050), simplified);
	}

	[Fact]
	public void SimplifyCapped_stops_at_400_metres()
	{
		// A zigzag whose every point is kilometres away from its neighbours: no tolerance under 400 m fits the cap.
		var line = new List<(double Latitude, double Longitude)>();

		for (int i = 0; i < 200; i++)
		{
			line.Add((51.0000 + (i % 2) * 0.1, 13.0000 + i * 0.01));
		}

		IReadOnlyList<(double Latitude, double Longitude)> simplified = PolylineSimplify.SimplifyCapped(line, 16, 25);

		// The cap is unreachable under the ceiling: the answer is whatever the last pass (400 m) kept.
		Assert.True(simplified.Count > 16);
		Assert.Equal(PolylineSimplify.Simplify(line, 400).Count, simplified.Count);
	}

	[Fact]
	public void MapScene_writes_the_polygon_node()
	{
		var scene =
			new MapScene
			{
				Polygons =
				[
					new MapPolygon(
						[(51.0, 13.0), (51.1, 13.0), (51.1, 13.1), (51.0, 13.1)],
						"@Accent|#0b6e8a",
						0.9,
						"@Outline|#9e9e9e",
						"10",
						51.05,
						13.05),

					// Two points are not an area: dropped.
					new MapPolygon([(51.2, 13.2), (51.3, 13.3)], "#ff0000", 0.1)
				],
				Fit = false
			};

		using JsonDocument document = JsonDocument.Parse(scene.ToJson(dark: false, color: _ => "#123456"));

		JsonElement root = document.RootElement;

		Assert.False(root.GetProperty("fit").GetBoolean());
		Assert.True(root.TryGetProperty("polygons", out JsonElement polygons));
		Assert.Equal(1, polygons.GetArrayLength());

		JsonElement zone = polygons[0];

		Assert.Equal("#123456", zone.GetProperty("color").GetString());
		Assert.Equal("#123456", zone.GetProperty("outline").GetString());
		Assert.Equal(0.5, zone.GetProperty("opacity").GetDouble());
		Assert.Equal("10", zone.GetProperty("label").GetString());
		Assert.Equal(51.05, zone.GetProperty("at")[0].GetDouble());
		Assert.Equal(13.05, zone.GetProperty("at")[1].GetDouble());
		Assert.Equal(4, zone.GetProperty("points").GetArrayLength());
	}

	[Fact]
	public void An_empty_scene_stays_empty_with_polygons()
	{
		Assert.False(new MapScene { Polygons = [new MapPolygon([(51.0, 13.0), (51.1, 13.0), (51.1, 13.1)], "#00ff00")] }.IsEmpty);
		Assert.True(new MapScene().IsEmpty);
	}
}
