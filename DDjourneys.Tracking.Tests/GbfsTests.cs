using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Shared;
using DDjourneys.Core.Providers.Vvo.Serialization;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The shared-bikes layer of Tier 2: reading GBFS 2.x (discovery in both language shapes, the two feeds,
/// the join). Everything goes through GbfsClient's stateless helpers; the samples are trimmed literals
/// shaped like the live MOBIbike feeds.
/// </summary>
public class GbfsTests
{
	private const string GermanDiscovery =
		"""
		{"last_updated":1696512000,"ttl":60,"data":{"de":{"feeds":[
			{"name":"system_information","url":"https://gbfs.nextbike.net/maps/gbfs/v2/nextbike_dx/gbfs.json"},
			{"name":"station_information","url":"https://gbfs.nextbike.net/maps/gbfs/v2/nextbike_dx/station_information.json"},
			{"name":"station_status","url":"https://gbfs.nextbike.net/maps/gbfs/v2/nextbike_dx/station_status.json"}
		]}}}
		""";

	private const string EnglishDiscovery =
		"""
		{"last_updated":1696512000,"ttl":60,"data":{"en":{"feeds":[
			{"name":"system_information","url":"https://data.lime.bike/api/partners/v2/gbfs/dresden/gbfs"},
			{"name":"station_information","url":"https://data.lime.bike/api/partners/v2/gbfs/dresden/station_information"}
		]}}}
		""";

	private const string Information =
		"""
		{"last_updated":1696512000,"ttl":3600,"data":{"stations":[
			{"station_id":"dd001","name":"Hauptbahnhof","short_name":"Hbf","lat":51.0293,"lon":13.7314,"region_id":"dresden","is_virtual":0,
			 "rental_uris":{"android":"nextbike://de?station_id=dd001","ios":"nextbike://ios/dd001","web":"https://www.nextbike.de/de/dresden/"}},
			{"station_id":"dd002","name":"Postplatz","lat":51.0512,"lon":13.7399},
			{"station_id":"dd003","name":"Albertplatz","lat":51.0575,"lon":13.7519,
			 "rental_uris":{"web":"https://www.nextbike.de/de/dresden/"}},
			{"station_id":"","name":"Ohne Kennung","lat":51.0,"lon":13.7},
			{"station_id":"dd005","name":"Ohne Position"}
		]}}
		""";

	private const string Status =
		"""
		{"last_updated":1696512060,"ttl":60,"data":{"stations":[
			{"station_id":"dd001","num_bikes_available":7,"vehicle_types_available":[{"vehicle_type_id":"bike","count":7}],
			 "num_docks_available":4,"is_installed":1,"is_renting":1,"is_returning":1,"last_reported":1696512050},
			{"station_id":"dd002","num_bikes_available":0,"num_docks_available":10,"is_installed":1,"is_renting":0,"is_returning":1,
			 "last_reported":1696512040},
			{"station_id":"dd099","num_bikes_available":5,"last_reported":1696512030}
		]}}
		""";

	private static T Read<T>(string json)
	{
		object? parsed = JsonSerializer.Deserialize(json, VvoJson.TypeInfo(typeof(T))!);

		return (T)parsed!;
	}

	[Fact]
	public void A_german_discovery_names_its_feeds()
	{
		GbfsDiscovery discovery = Read<GbfsDiscovery>(GermanDiscovery);

		Uri? information = GbfsClient.FeedUrl(discovery, "station_information");
		Uri? status = GbfsClient.FeedUrl(discovery, "station_status");

		Assert.NotNull(information);
		Assert.NotNull(status);
		Assert.Equal("https://gbfs.nextbike.net/maps/gbfs/v2/nextbike_dx/station_information.json", information!.ToString());
		Assert.Equal("https://gbfs.nextbike.net/maps/gbfs/v2/nextbike_dx/station_status.json", status!.ToString());
	}

	[Fact]
	public void An_english_discovery_is_read_the_same_way()
	{
		GbfsDiscovery discovery = Read<GbfsDiscovery>(EnglishDiscovery);

		Assert.NotNull(GbfsClient.FeedUrl(discovery, "station_information"));
		// This operator names no station_status feed (Lime is dockless): a missing feed is a missing feed.
		Assert.Null(GbfsClient.FeedUrl(discovery, "station_status"));
	}

	[Fact]
	public void Stations_are_joined_by_id_and_survive_a_missing_status()
	{
		GbfsStationInformation information = Read<GbfsStationInformation>(Information);
		GbfsStationStatus status = Read<GbfsStationStatus>(Status);

		IReadOnlyList<SharedStation> joined = GbfsClient.Join(information, status, "MOBIbike");

		Assert.Equal(3, joined.Count);

		SharedStation hauptbahnhof = joined[0];
		Assert.Equal("dd001", hauptbahnhof.StationId);
		Assert.Equal(7, hauptbahnhof.Bikes);
		Assert.Equal(4, hauptbahnhof.Docks);
		Assert.True(hauptbahnhof.IsRenting);
		Assert.NotNull(hauptbahnhof.UpdatedAt);
		Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1696512050000L), hauptbahnhof.UpdatedAt);

		// Postplatz reports but is not renting: the station stands, grey.
		SharedStation postplatz = joined[1];
		Assert.Equal(0, postplatz.Bikes);
		Assert.Equal(10, postplatz.Docks);
		Assert.False(postplatz.IsRenting);

		// Albertplatz has no status row at all: counts unknown, not zero-known.
		SharedStation albertplatz = joined[2];
		Assert.Equal(0, albertplatz.Bikes);
		Assert.Null(albertplatz.IsRenting);
		Assert.Null(albertplatz.UpdatedAt);
	}

	[Fact]
	public void Stations_without_an_id_or_a_position_are_skipped_and_orphans_are_dropped()
	{
		GbfsStationInformation information = Read<GbfsStationInformation>(Information);
		GbfsStationStatus status = Read<GbfsStationStatus>(Status);

		IReadOnlyList<SharedStation> joined = GbfsClient.Join(information, status, "MOBIbike");

		Assert.DoesNotContain(joined, station => station.Name is "Ohne Kennung" or "Ohne Position");
		Assert.DoesNotContain(joined, station => station.StationId == "dd099");
	}

	[Fact]
	public void A_failed_status_feed_leaves_the_skeleton_standing_without_numbers()
	{
		GbfsStationInformation information = Read<GbfsStationInformation>(Information);

		IReadOnlyList<SharedStation> joined = GbfsClient.Join(information, null, "MOBIbike");

		Assert.Equal(3, joined.Count);
		Assert.All(joined, static station => Assert.Equal(0, station.Bikes));
		Assert.All(joined, static station => Assert.Null(station.IsRenting));
	}

	[Fact]
	public void Rental_links_prefer_the_app_and_fall_back_to_the_web()
	{
		GbfsStationInformation information = Read<GbfsStationInformation>(Information);
		GbfsStationStatus status = Read<GbfsStationStatus>(Status);

		IReadOnlyList<SharedStation> joined = GbfsClient.Join(information, status, "MOBIbike");

		SharedStation hauptbahnhof = joined[0];
		Assert.Equal("nextbike://de/?station_id=dd001", hauptbahnhof.AppUri!.ToString());
		Assert.Equal("https://www.nextbike.de/de/dresden/", hauptbahnhof.WebUri!.ToString());

		SharedStation postplatz = joined[1];
		Assert.Null(postplatz.AppUri);
		Assert.Null(postplatz.WebUri);

		SharedStation albertplatz = joined[2];
		Assert.Null(albertplatz.AppUri);
		Assert.Equal("https://www.nextbike.de/de/dresden/", albertplatz.WebUri!.ToString());
	}

	/// <summary>Trimmed from the live MOBIbike 2.3 feeds (October 2026): flags are JSON booleans, most stations are virtual.</summary>
	private const string LiveInformation =
		"""
		{"last_updated":1791679074,"ttl":60,"version":"2.3","data":{"stations":[
			{"station_id":"6768","name":"MOBIpunkt Fetscherplatz","short_name":"43004","lat":51.046605,"lon":13.771298,"region_id":"685",
			 "rental_uris":{"android":"https://app.nextbike.net/station?id=6768","ios":"https://app.nextbike.net/station?id=6768","web":"https://nxtb.it/p/6768"},
			 "is_virtual_station":false,"capacity":8},
			{"station_id":"4711","name":"Albertplatz","short_name":"43100","lat":51.0638,"lon":13.7477,"region_id":"685","is_virtual_station":true,
			 "station_area":{"type":"MultiPolygon","coordinates":[]}}
		]}}
		""";

	private const string LiveStatus =
		"""
		{"last_updated":1791679074,"ttl":60,"version":"2.3","data":{"stations":[
			{"station_id":"6768","num_bikes_available":2,"vehicle_types_available":[{"vehicle_type_id":"196","count":1},{"vehicle_type_id":"275","count":1}],
			 "num_docks_available":6,"is_installed":true,"is_renting":true,"is_returning":true,"last_reported":1791679074},
			{"station_id":"4711","num_bikes_available":0,"vehicle_types_available":[],"num_docks_available":0,"is_installed":true,"is_renting":true,"is_returning":true,"last_reported":1791679074}
		]}}
		""";

	private const string LiveTypes =
		"""
		{"last_updated":1791679074,"ttl":60,"data":{"vehicle_types":[
			{"vehicle_type_id":"196","name":"Classic Bike","form_factor":"bicycle","rider_capacity":1,"propulsion_type":"human"},
			{"vehicle_type_id":"275","name":"E-Bike","form_factor":"bicycle","rider_capacity":1,"propulsion_type":"electric_assist"},
			{"vehicle_type_id":"1","form_factor":"scooter","propulsion_type":"electric","max_range_meters":24140}
		]}}
		""";

	private const string LiveFree =
		"""
		{"last_updated":1791679068,"ttl":60,"version":"2.2","data":{"bikes":[
			{"bike_id":"at-station","lat":51.0466,"lon":13.7713,"is_reserved":false,"is_disabled":false,"vehicle_type_id":"196","station_id":"6768"},
			{"bike_id":"free-1","lat":51.0293,"lon":13.7191,"is_reserved":false,"is_disabled":false,"current_range_meters":16061,"vehicle_type_id":"3","last_reported":1791679068,"vehicle_type":"e-bike"},
			{"bike_id":"free-2","lat":51.03,"lon":13.72,"is_reserved":false,"is_disabled":false,"vehicle_type_id":"1"},
			{"bike_id":"reserved","lat":51.03,"lon":13.72,"is_reserved":true,"is_disabled":false},
			{"bike_id":"broken","lat":51.03,"lon":13.72,"is_reserved":false,"is_disabled":1}
		]}}
		""";

	[Fact]
	public void Boolean_flags_virtual_stations_and_e_bikes_are_read()
	{
		IReadOnlyList<SharedStation> joined = GbfsClient.Join(
			Read<GbfsStationInformation>(LiveInformation),
			Read<GbfsStationStatus>(LiveStatus),
			"MOBIbike",
			types: Read<GbfsVehicleTypes>(LiveTypes),
			operatorId: "nextbike",
			appPackage: "de.nextbike");

		Assert.Equal(2, joined.Count);

		SharedStation punkt = joined[0];
		Assert.False(punkt.IsVirtual);
		Assert.Equal(2, punkt.Bikes);
		Assert.Equal(1, punkt.EBikes);
		Assert.Equal(6, punkt.Docks);
		Assert.Equal(8, punkt.Capacity);
		Assert.True(punkt.IsRenting);
		Assert.True(punkt.HasStatus);
		Assert.Equal("de.nextbike", punkt.AppPackage);
		Assert.Equal("https://app.nextbike.net/station?id=6768", punkt.AppUri!.ToString());

		SharedStation area = joined[1];
		Assert.True(area.IsVirtual);
		Assert.Equal(0, area.Bikes);
		Assert.Null(area.Docks);
		Assert.Null(area.Capacity);
	}

	[Fact]
	public void Free_vehicles_leave_out_the_docked_the_reserved_and_the_disabled()
	{
		IReadOnlyList<SharedVehicle> vehicles = GbfsClient.Vehicles(
			Read<GbfsFreeBikeStatus>(LiveFree),
			Read<GbfsVehicleTypes>(LiveTypes),
			"lime",
			"Lime",
			appPackage: "com.limebike");

		Assert.Equal(new[] { "free-1", "free-2" }, vehicles.Select(vehicle => vehicle.Id));

		// No type "3" in the list: Lime's own word decides.
		Assert.Equal(SharedKind.EBike, vehicles[0].Kind);
		Assert.Equal(16061, vehicles[0].RangeMeters);
		Assert.Equal(SharedKind.Scooter, vehicles[1].Kind);
		Assert.All(vehicles, static vehicle => Assert.Equal("com.limebike", vehicle.AppPackage));
	}

	[Fact]
	public void Posix_timestamps_are_told_apart_by_their_size()
	{
		Assert.Equal(
			DateTimeOffset.FromUnixTimeMilliseconds(1696512050000L),
			GbfsClient.FromPosix(1696512050L));
		Assert.Equal(
			DateTimeOffset.FromUnixTimeMilliseconds(1762255220000L),
			GbfsClient.FromPosix(1762255220000L));
	}
}
