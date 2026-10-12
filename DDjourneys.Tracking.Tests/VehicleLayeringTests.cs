using DDjourneys.Core.Mapping;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>The pure part of the vehicles layer: what a viewport keeps of the vehicles around it.</summary>
public class VehicleLayeringTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 5, 7, 20, 0, TimeSpan.FromHours(2));

	private static LiveVehicle At(
		double latitude,
		double longitude,
		int line = 8,
		int minutesAgo = 0,
		int run = 1) =>
		new()
		{
			Line = line,
			Run = run,
			Latitude = latitude,
			Longitude = longitude,
			Time = Now - TimeSpan.FromMinutes(minutesAgo)
		};

	private static MapViewport View(double south = 51.0, double north = 51.1, double west = 13.7, double east = 13.8, double lat = 51.05, double lon = 13.75) =>
		new(south, west, north, east, 14, lat, lon);

	[Fact]
	public void Vehicles_inside_the_bounds_are_kept()
	{
		LiveVehicle[] vehicles = [At(51.05, 13.75), At(51.02, 13.72), At(51.09, 13.79)];

		IReadOnlyList<LiveVehicle> within = VehicleLayering.Within(View(), vehicles, now: Now);

		Assert.Equal(3, within.Count);
	}

	[Fact]
	public void Vehicles_outside_the_bounds_are_dropped_but_the_margin_slides_them_in()
	{
		LiveVehicle[] vehicles =
		[
			At(50.95, 13.75),   // far south: out
			At(50.995, 13.75),  // just south of the bounds, inside the tenth-of-a-span margin: kept
			At(51.2, 13.75)     // far north: out
		];

		IReadOnlyList<LiveVehicle> within = VehicleLayering.Within(View(), vehicles, now: Now);

		Assert.Single(within);
		Assert.Equal(50.995, within[0].Latitude);
	}

	[Fact]
	public void Positions_older_than_the_age_limit_are_pruned()
	{
		LiveVehicle[] vehicles = [At(51.05, 13.75, minutesAgo: 0), At(51.06, 13.76, minutesAgo: 2)];

		IReadOnlyList<LiveVehicle> within = VehicleLayering.Within(View(), vehicles, maxAge: TimeSpan.FromMinutes(1), now: Now);

		Assert.Single(within);
		Assert.Equal(51.05, within[0].Latitude);
	}

	[Fact]
	public void Too_many_vehicles_keep_the_nearest_to_the_centre()
	{
		LiveVehicle[] vehicles =
		[
			At(51.05, 13.75),  // at the centre
			At(51.051, 13.751),
			At(51.052, 13.752),
			At(51.08, 13.78)   // the farthest
		];

		IReadOnlyList<LiveVehicle> within = VehicleLayering.Within(View(), vehicles, cap: 3, now: Now);

		Assert.Equal(3, within.Count);
		Assert.DoesNotContain(within, vehicle => vehicle.Latitude == 51.08);
		Assert.Equal(51.05, within[0].Latitude);
		Assert.Equal(51.051, within[1].Latitude);
	}

	[Fact]
	public void Nothing_around_leaves_nothing()
	{
		Assert.Empty(VehicleLayering.Within(View(), [], now: Now));
		Assert.Empty(VehicleLayering.Within(View(), [At(51.05, 13.75, minutesAgo: 30)], maxAge: TimeSpan.FromSeconds(90), now: Now));
	}
}
