using DDjourneys.Core.Mapping;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The offline city map of Dresden (B7): the standard Web Mercator tile math, the fixed box with its zoom
/// range, the enumeration order, the honest size estimate and what "within the set" means for the map's
/// offline serving.
/// </summary>
public sealed class CityTilesTests
{
	[Fact]
	public void The_tile_of_a_position_is_the_standard_slippy_map_math()
	{
		// The map's own default centre (Postplatz area), checked against the published formulas.
		Assert.Equal((17634, 10962), CityTiles.TileOf(51.0504, 13.7373, 15));
		Assert.Equal((551, 342), CityTiles.TileOf(51.0504, 13.7373, 10));

		// The equator sits at half the map, the whole world is one tile at zero.
		Assert.Equal((0, 4), CityTiles.TileOf(0, -180, 3));
		Assert.Equal((4, 4), CityTiles.TileOf(0, 0, 3));
		Assert.Equal((0, 0), CityTiles.TileOf(0, 0, 0));
		Assert.Equal((0, 0), CityTiles.TileOf(51.0504, 13.7373, 0));
	}

	[Fact]
	public void Positions_beyond_the_projection_are_clamped_onto_its_edge()
	{
		Assert.Equal(CityTiles.TileOf(85.05112878, 13.7, 10), CityTiles.TileOf(89, 13.7, 10));
		Assert.Equal(CityTiles.TileOf(-85.05112878, 13.7, 10), CityTiles.TileOf(-89, 13.7, 10));
	}

	[Fact]
	public void The_set_is_the_fixed_dresden_box_at_ten_to_fifteen()
	{
		// The box the plan fixed: the city and its close outskirts (the centre above sits inside it).
		Assert.Equal(51.00, CityTiles.SouthLatitude);
		Assert.Equal(51.11, CityTiles.NorthLatitude);
		Assert.Equal(13.63, CityTiles.WestLongitude);
		Assert.Equal(13.85, CityTiles.EastLongitude);
		Assert.Equal(10, CityTiles.MinZoom);
		Assert.Equal(15, CityTiles.MaxZoom);

		// 469 tiles: two of the whole city at z10, street detail at z15.
		Assert.Equal(469, CityTiles.Count());
		Assert.Equal(2, CityTiles.Count(10, 10));
		Assert.Equal(336, CityTiles.Count(15, 15));
		Assert.Equal(CityTiles.Count(), CityTiles.Tiles().Count());
	}

	[Fact]
	public void Tiles_come_shallowest_first_and_rows_north_to_south()
	{
		(int Zoom, int X, int Y)[] tiles = [.. CityTiles.Tiles().Select(tile => (tile.Zoom, tile.X, tile.Y))];

		// The overview arrives before the detail; within a level the columns run west to east per row.
		Assert.Equal((10, 550, 342), tiles[0]);
		Assert.Equal((10, 551, 342), tiles[1]);
		Assert.Equal((11, 1101, 684), tiles[2]);

		(int Zoom, int X, int Y)[] sorted = [.. tiles.OrderBy(tile => tile.Zoom).ThenBy(tile => tile.Y).ThenBy(tile => tile.X)];

		Assert.Equal(sorted, tiles);
	}

	[Fact]
	public void Every_tile_of_the_set_is_within_the_set_and_its_edges_are_honest()
	{
		Assert.All(
			CityTiles.Tiles(),
			tile => Assert.True(CityTiles.Within(tile.Zoom, tile.X, tile.Y), $"{tile.Zoom}/{tile.X}/{tile.Y}"));

		// One tile beyond each edge of the deepest level: the map's own miss, never a stretched picture.
		Assert.False(CityTiles.Within(15, 17623, 10962));
		Assert.False(CityTiles.Within(15, 17645, 10962));
		Assert.False(CityTiles.Within(15, 17634, 10953));
		Assert.False(CityTiles.Within(15, 17634, 10970));
	}

	[Fact]
	public void Levels_outside_the_kept_range_answer_nothing()
	{
		// Deeper than 15 keeps coming from the network; shallower than 10 is nothing the map shows.
		Assert.False(CityTiles.Within(9, 551, 342));
		Assert.False(CityTiles.Within(16, 17634, 10962));
		Assert.Equal(0, CityTiles.Count(16, 16));
		Assert.Equal(0, CityTiles.Count(9, 9));
	}

	[Fact]
	public void The_estimate_is_the_count_times_the_average_tile_weight()
	{
		// About 9 MB for today's set, said as one honest number before the download.
		Assert.Equal(469L * CityTiles.BytesPerTile, CityTiles.EstimateBytes());
		Assert.Equal(9L, CityTiles.EstimateBytes() / 1024 / 1024);
		Assert.Equal((long)CityTiles.Count(12, 12) * CityTiles.BytesPerTile, CityTiles.EstimateBytes(12, 12));
	}

	[Fact]
	public void Zoom_arguments_are_clamped_into_the_kept_range_and_a_swapped_range_still_answers()
	{
		Assert.Equal(CityTiles.Count(), CityTiles.Count(8, 99));
		Assert.Equal(127, CityTiles.Count(12, 14));

		// A swapped range names the same levels.
		Assert.Equal(CityTiles.Count(12, 14), CityTiles.Count(14, 12));

		(int Zoom, int X, int Y)[] swapped = [.. CityTiles.Tiles(14, 12).Select(tile => (tile.Zoom, tile.X, tile.Y))];
		(int Zoom, int X, int Y)[] straight = [.. CityTiles.Tiles(12, 14).Select(tile => (tile.Zoom, tile.X, tile.Y))];

		Assert.Equal(straight, swapped);
	}

	[Fact]
	public void The_corners_name_the_first_and_last_columns_and_rows()
	{
		// The north-west corner opens the level, the south-east corner closes it.
		Assert.Equal((2203, 1369), CityTiles.TileOf(CityTiles.NorthLatitude, CityTiles.WestLongitude, 12));
		Assert.Equal((2205, 1371), CityTiles.TileOf(CityTiles.SouthLatitude, CityTiles.EastLongitude, 12));

		Assert.True(CityTiles.Within(12, 2203, 1369));
		Assert.True(CityTiles.Within(12, 2205, 1371));
		Assert.False(CityTiles.Within(12, 2202, 1369));
		Assert.False(CityTiles.Within(12, 2206, 1371));
	}
}
