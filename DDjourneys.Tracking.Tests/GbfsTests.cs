using System.Text.Json;
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
		object? parsed = JsonSerializer.Deserialize(json, VvoJson.TypeInfo(typeof(T)))!;

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
		Assert.Equal("nextbike://de?station_id=dd001", hauptbahnhof.AppUri!.ToString());
		Assert.Equal("https://www.nextbike.de/de/dresden/", hauptbahnhof.WebUri!.ToString());

		SharedStation postplatz = joined[1];
		Assert.Null(postplatz.AppUri);
		Assert.Null(postplatz.WebUri);

		SharedStation albertplatz = joined[2];
		Assert.Null(albertplatz.AppUri);
		Assert.Equal("https://www.nextbike.de/de/dresden/", albertplatz.WebUri!.ToString());
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
