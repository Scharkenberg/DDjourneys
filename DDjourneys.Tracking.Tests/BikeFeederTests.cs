using DDjourneys.Core.Api;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The bike feeder: reading a BRouter track, the boarding choice, the composed journey, and how the
/// journey search falls back to the ordinary answer when no bike track can be planned.
/// </summary>
public sealed class BikeFeederTests
{
	private static readonly DateTimeOffset Day = new(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(2));

	/// <summary>A real BRouter answer (2026-10-12, brouter.de, profile trekking), shortened to five points.</summary>
	private const string TrackJson =
		"{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":"
		+ "{\"track-length\":\"1102\",\"total-time\":\"187\",\"filtered ascend\":\"2\",\"plain-ascend\":\"-1\"},"
		+ "\"geometry\":{\"type\":\"LineString\",\"coordinates\":["
		+ "[13.737238,51.050405,113.0],[13.737239,51.050408,113.0],[13.737264,51.050494,113.0],"
		+ "[13.747847,51.053011,111.75],[13.747997,51.053017,111.0]]}}]}";

	[Fact]
	public void The_track_request_carries_the_points_longitude_first()
	{
		string query = BrouterClient.Query(51.050405, 13.737238, 51.053017, 13.747997);

		Assert.Equal(
			"lonlats=13.737238,51.050405|13.747997,51.053017&profile=trekking&alternativeidx=0&format=geojson",
			query);
	}

	[Fact]
	public void A_track_is_read_with_its_hills()
	{
		BikeRoute? route = BrouterClient.Parse(TrackJson);

		Assert.NotNull(route);
		Assert.Equal(1102, route.DistanceMeters);
		Assert.Equal(TimeSpan.FromSeconds(187), route.Duration);
		Assert.Equal(2, route.AscentMeters);

		// The descent is the filtered ascent minus the height difference of the ends: 2 - (111 - 113).
		Assert.Equal(4, route.DescentMeters);

		Assert.Equal(5, route.Path.Count);
		Assert.Equal((51.050405, 13.737238), route.Path[0]);
		Assert.Equal((51.053017, 13.747997), route.Path[^1]);
	}

	[Fact]
	public void A_track_that_only_climbs_descends_nothing()
	{
		string json =
			"{\"features\":[{\"properties\":{\"track-length\":\"900\",\"total-time\":\"120\",\"filtered ascend\":\"0\"},"
			+ "\"geometry\":{\"coordinates\":[[13.7,51.05,110.0],[13.71,51.05,113.0]]}}]}";

		BikeRoute? route = BrouterClient.Parse(json);

		Assert.NotNull(route);
		Assert.Equal(0, route.AscentMeters);
		Assert.Equal(0, route.DescentMeters);
	}

	[Fact]
	public void A_track_that_cannot_say_what_it_is_is_not_read()
	{
		Assert.Null(BrouterClient.Parse("not json at all"));

		Assert.Null(BrouterClient.Parse("{\"features\":[]}"));

		// Without the heights of the ends the track cannot say what it climbs.
		Assert.Null(
			BrouterClient.Parse(
				"{\"features\":[{\"properties\":{\"track-length\":\"900\",\"total-time\":\"120\",\"filtered ascend\":\"5\"},"
				+ "\"geometry\":{\"coordinates\":[[13.7,51.05],[13.71,51.06]]}}]}"));

		// Without the numbers of the service there is no track.
		Assert.Null(
			BrouterClient.Parse(
				"{\"features\":[{\"properties\":{\"total-time\":\"120\",\"filtered ascend\":\"5\"},"
				+ "\"geometry\":{\"coordinates\":[[13.7,51.05,110.0],[13.71,51.06,111.0]]}}]}"));
	}

	[Fact]
	public void The_boarding_stop_is_the_flattest_of_the_comparable_rides()
	{
		Location near = Place("1", "Near", PlaceKind.Stop);
		Location flat = Place("2", "Flat", PlaceKind.Stop);
		Location far = Place("3", "Far", PlaceKind.Stop);

		var candidates = new (Location Stop, BikeRoute Route)[]
		{
			(near, Track(minutes: 6, ascent: 40)),
			(flat, Track(minutes: 7, ascent: 10)),
			(far, Track(minutes: 20, ascent: 0))
		};

		(Location Stop, BikeRoute Route) boarding = BikeFeeder.Board(candidates);

		// The flat ride wins although the nearest is faster; the far one is not comparable any more.
		Assert.Equal("2", boarding.Stop.Id);
	}

	[Fact]
	public void Among_equally_flat_rides_the_faster_one_wins()
	{
		Location one = Place("1", "One", PlaceKind.Stop);
		Location two = Place("2", "Two", PlaceKind.Stop);

		(Location Stop, BikeRoute Route) boarding = BikeFeeder.Board(
			[
				(one, Track(minutes: 9, ascent: 12)),
				(two, Track(minutes: 6, ascent: 12))
			]);

		Assert.Equal("2", boarding.Stop.Id);
	}

	[Fact]
	public void The_radius_stays_inside_its_range()
	{
		Assert.Equal(1000, BikeFeeder.RadiusMeters(new RoutingPreferences { BikeFeederRadiusKm = 0 }));
		Assert.Equal(3000, BikeFeeder.RadiusMeters(new RoutingPreferences { BikeFeederRadiusKm = 3 }));
		Assert.Equal(5000, BikeFeeder.RadiusMeters(new RoutingPreferences { BikeFeederRadiusKm = 9 }));
	}

	[Fact]
	public void The_feeder_needs_the_switch_a_place_and_coordinates()
	{
		Assert.False(BikeFeeder.Applies(Query()));

		// A place without coordinates cannot be the start of a bike track.
		Assert.False(
			BikeFeeder.Applies(
				Query(
					from: Place("addr:1", "Somewhere", PlaceKind.Address),
					routing: new RoutingPreferences { BikeFeeder = true })));

		// A stop needs no bike leg: the passenger boards there.
		Assert.False(
			BikeFeeder.Applies(
				Query(
					from: Place("33000742", "Postplatz", PlaceKind.Stop, 51.05, 13.72),
					routing: new RoutingPreferences { BikeFeeder = true })));

		Assert.True(
			BikeFeeder.Applies(
				Query(
					from: Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79),
					routing: new RoutingPreferences { BikeFeeder = true })));
	}

	[Fact]
	public async Task A_departure_search_gets_the_bike_leg_fitted_in_front()
	{
		Location start = Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79);
		Location boardingStop = Place("33000742", "Postplatz", PlaceKind.Stop, 51.05, 13.72);

		JourneyQuery? asked = null;

		Task<JourneyResult> Search(JourneyQuery query, CancellationToken token)
		{
			asked = query;

			return Task.FromResult(
				JourneyResult.Success(
					[
						Trip(
							Ride(boardingStop, Place("3", "Pirnaischer Platz", PlaceKind.Stop), "61", 10, 30, 8))
					]));
		}

		BikeFeederResult result =
			await BikeFeeder.ComposeAsync(
				Search,
				(lat, lon, radius, token) => Task.FromResult<IReadOnlyList<NearbyStop>>(
					[new NearbyStop { Stop = boardingStop, DistanceMeters = 800 }]),
				(lat, lon, toLat, toLon, token) => Task.FromResult<BikeRoute?>(Track(minutes: 10, ascent: 5)),
				Query(from: start, routing: new RoutingPreferences { BikeFeeder = true }));

		Assert.Equal(BikeFeederOutcome.Composed, result.Outcome);
		Assert.Equal("Postplatz", result.BoardingStop);

		// The transit part leaves from the boarding stop once the bike is there and parked.
		Assert.NotNull(asked);
		Assert.Equal("33000742", asked.From.Id);
		Assert.Equal(Day.AddHours(10).AddMinutes(12), asked.DateTime);
		Assert.Equal(JourneySearchMode.Departure, asked.SearchMode);

		Journey journey = Assert.Single(result.Journeys);
		Assert.True(BikeFeeder.IsFeeder(journey));

		Assert.Equal(2, journey.Legs.Count);
		Assert.Equal(TransitMode.Bike, journey.Legs[0].Mode);
		Assert.Equal(1000, journey.Legs[0].DistanceMeters);
		Assert.Equal(5, journey.Legs[0].AscentMeters);

		// The bike arrives one boarding buffer before the vehicle leaves, and starts one ride earlier.
		Assert.Equal(Day.AddHours(10).AddMinutes(28), journey.Legs[0].ScheduledArrival);
		Assert.Equal(Day.AddHours(10).AddMinutes(18), journey.Legs[0].ScheduledDeparture);

		Assert.Equal(start.Name, journey.From.Name);
		Assert.Equal(start.Name, journey.Origin?.Name);

		// The bike is not a ride: one tram, no change.
		Assert.False(TransitMode.Bike.IsRide());
		Assert.Single(journey.Rides);
		Assert.Equal(0, journey.TransferCount);
	}

	[Fact]
	public async Task A_bike_ride_that_would_start_before_the_asked_time_is_dropped()
	{
		Location start = Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79);
		Location boardingStop = Place("33000742", "Postplatz", PlaceKind.Stop, 51.05, 13.72);

		// The vehicle leaves at 10:05; the bike would have to start at 09:53, before the asked 10:00.
		Task<JourneyResult> Search(JourneyQuery query, CancellationToken token) =>
			Task.FromResult(
				JourneyResult.Success(
					[Trip(Ride(boardingStop, Place("3", "Pirnaischer Platz", PlaceKind.Stop), "61", 10, 5, 8))]));

		BikeFeederResult result =
			await BikeFeeder.ComposeAsync(
				Search,
				(lat, lon, radius, token) => Task.FromResult<IReadOnlyList<NearbyStop>>(
					[new NearbyStop { Stop = boardingStop, DistanceMeters = 800 }]),
				(lat, lon, toLat, toLon, token) => Task.FromResult<BikeRoute?>(Track(minutes: 10, ascent: 5)),
				Query(from: start, routing: new RoutingPreferences { BikeFeeder = true }));

		Assert.Equal(BikeFeederOutcome.NoTransit, result.Outcome);
		Assert.Empty(result.Journeys);
	}

	[Fact]
	public async Task An_arrival_search_keeps_the_asked_time_and_fits_the_bike_in_front()
	{
		Location start = Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79);
		Location boardingStop = Place("33000742", "Postplatz", PlaceKind.Stop, 51.05, 13.72);

		JourneyQuery? asked = null;

		Task<JourneyResult> Search(JourneyQuery query, CancellationToken token)
		{
			asked = query;

			return Task.FromResult(
				JourneyResult.Success(
					[Trip(Ride(boardingStop, Place("3", "Pirnaischer Platz", PlaceKind.Stop), "61", 10, 30, 8))]));
		}

		BikeFeederResult result =
			await BikeFeeder.ComposeAsync(
				Search,
				(lat, lon, radius, token) => Task.FromResult<IReadOnlyList<NearbyStop>>(
					[new NearbyStop { Stop = boardingStop, DistanceMeters = 800 }]),
				(lat, lon, toLat, toLon, token) => Task.FromResult<BikeRoute?>(Track(minutes: 10, ascent: 5)),
				Query(
					from: start,
					routing: new RoutingPreferences { BikeFeeder = true },
					mode: JourneySearchMode.Arrival,
					time: Day.AddHours(11)));

		Assert.Equal(BikeFeederOutcome.Composed, result.Outcome);

		Assert.NotNull(asked);
		Assert.Equal(JourneySearchMode.Arrival, asked.SearchMode);
		Assert.Equal(Day.AddHours(11), asked.DateTime);

		Journey journey = Assert.Single(result.Journeys);
		Assert.Equal(Day.AddHours(10).AddMinutes(18), journey.Legs[0].ScheduledDeparture);
	}

	[Fact]
	public async Task Without_any_bike_track_the_feeder_says_so()
	{
		Location start = Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79);

		BikeFeederResult result =
			await BikeFeeder.ComposeAsync(
				(query, token) => Task.FromResult(JourneyResult.Success([Trip(Ride())])),
				(lat, lon, radius, token) => Task.FromResult<IReadOnlyList<NearbyStop>>([]),
				(lat, lon, toLat, toLon, token) => Task.FromResult<BikeRoute?>(null),
				Query(from: start, routing: new RoutingPreferences { BikeFeeder = true }));

		Assert.Equal(BikeFeederOutcome.NoBikeRoute, result.Outcome);
		Assert.Empty(result.Journeys);
	}

	[Fact]
	public async Task Without_a_transit_journey_the_feeder_stays_quiet()
	{
		Location start = Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79);
		Location boardingStop = Place("33000742", "Postplatz", PlaceKind.Stop, 51.05, 13.72);

		BikeFeederResult result =
			await BikeFeeder.ComposeAsync(
				(query, token) => Task.FromResult(JourneyResult.Success([])),
				(lat, lon, radius, token) => Task.FromResult<IReadOnlyList<NearbyStop>>(
					[new NearbyStop { Stop = boardingStop, DistanceMeters = 800 }]),
				(lat, lon, toLat, toLon, token) => Task.FromResult<BikeRoute?>(Track(minutes: 10, ascent: 5)),
				Query(from: start, routing: new RoutingPreferences { BikeFeeder = true }));

		Assert.Equal(BikeFeederOutcome.NoTransit, result.Outcome);
		Assert.Equal("Postplatz", result.BoardingStop);
	}

	[Fact]
	public void The_notice_of_the_unavailable_feeder_is_added_once()
	{
		Journey journey = Trip(Ride());

		Journey marked = BikeFeeder.WithNotice(BikeFeeder.WithNotice(journey, BikeFeeder.UnavailableNotice), BikeFeeder.UnavailableNotice);

		Journey legNotices = BikeFeeder.WithNotice(journey, "a notice of a leg");

		Assert.Equal(["dd: bike-feeder-unavailable"], marked.Notices);
		Assert.Equal(["a notice of a leg"], legNotices.Notices);

		// The journey keeps what a provider journey carries.
		Assert.Equal(journey.Id, marked.Id);
		Assert.Equal(journey.Context, marked.Context);
	}

	[Fact]
	public async Task A_search_without_any_stop_around_answers_with_the_ordinary_journeys_and_the_marker()
	{
		Location start = Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79);
		Location destination = Place("33000007", "Pirnaischer Platz", PlaceKind.Stop, 51.05, 13.74);

		var provider = new OneProvider(
			(query, token) => Task.FromResult(JourneyResult.Success([Trip(Ride())])));

		var stops = new StopAreaService([], new LocationService([]), new ProviderRegistry([]));

		using var api = new ApiClient();
		var brouter = new BrouterClient(api);

		var service = new JourneyService([provider], stops: stops, brouter: brouter);

		JourneyResult result =
			await service.SearchAsync(
				Query(from: start, to: destination, routing: new RoutingPreferences { BikeFeeder = true }));

		Assert.Equal(JourneyOutcome.Found, result.Outcome);

		Journey journey = Assert.Single(result.Journeys);
		Assert.False(BikeFeeder.IsFeeder(journey));
		Assert.Contains(BikeFeeder.UnavailableNotice, journey.Notices);
	}

	private sealed class OneProvider(Func<JourneyQuery, CancellationToken, Task<JourneyResult>> search) : IJourneyProvider
	{
		public Task<JourneyResult> SearchAsync(JourneyQuery query, CancellationToken cancellationToken = default) =>
			search(query, cancellationToken);
	}

	private static BikeRoute Track(int minutes, int ascent) =>
		new(
			1000,
			TimeSpan.FromMinutes(minutes),
			ascent,
			Math.Max(0, ascent - 2),
			[(51.06, 13.79), (51.05, 13.72)]);

	private static JourneyQuery Query(
		Location? from = null,
		Location? to = null,
		RoutingPreferences? routing = null,
		JourneySearchMode mode = JourneySearchMode.Departure,
		DateTimeOffset? time = null) =>
		new()
		{
			From = from ?? Place("addr:1", "Bautzner Landstra\u00dfe 1", PlaceKind.Address, 51.06, 13.79),
			To = to ?? Place("33000007", "Pirnaischer Platz", PlaceKind.Stop, 51.05, 13.74),
			DateTime = time ?? Day.AddHours(10),
			SearchMode = mode,
			MaxResults = 3,
			Routing = routing ?? RoutingPreferences.Default
		};

	private static Location Place(string id, string name, PlaceKind kind, double? latitude = null, double? longitude = null) =>
		new()
		{
			Id = id,
			Name = name,
			Kind = kind,
			Latitude = latitude,
			Longitude = longitude
		};

	private static JourneyLeg Ride() =>
		Ride(Place("33000742", "Postplatz", PlaceKind.Stop, 51.05, 13.72), Place("33000007", "Pirnaischer Platz", PlaceKind.Stop, 51.05, 13.74), "61", 10, 30, 8);

	private static JourneyLeg Ride(Location from, Location to, string line, int hour, int minute, int minutes)
	{
		DateTimeOffset start = Day.AddHours(hour).AddMinutes(minute);

		return new JourneyLeg
		{
			Mode = TransitMode.Tram,
			From = StationOf(from),
			To = StationOf(to),
			Line = new TransitLine { Name = line, Mode = TransitMode.Tram },
			ScheduledDeparture = start,
			ScheduledArrival = start.AddMinutes(minutes)
		};
	}

	private static Journey Trip(params JourneyLeg[] legs) =>
		new()
		{
			Legs = legs,
			From = legs[0].From,
			To = legs[^1].To,
			Id = "route",
			Context = "session"
		};

	private static Station StationOf(Location place) =>
		new()
		{
			Id = place.Id ?? string.Empty,
			Name = place.Name,
			Latitude = place.Latitude,
			Longitude = place.Longitude
		};
}
