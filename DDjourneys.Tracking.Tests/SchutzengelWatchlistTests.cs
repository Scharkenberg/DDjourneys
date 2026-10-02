using System.Net;
using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;
using DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

namespace DDjourneys.Tracking.Tests;

public sealed class SchutzengelWatchlistTests
{
	private static readonly DateTimeOffset Base = new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);

	// ----- Trip progress -----

	[Fact]
	public void Progress_follows_the_clock_between_stops()
	{
		TripTimeline timeline = Parse(Trip(rideOneArrival: 10, secondDeparture: 15, realtimeArrival: null));

		TripSnapshot before = timeline.Calculate(Base.AddMinutes(-5));
		TripSnapshot riding = timeline.Calculate(Base.AddMinutes(3));
		TripSnapshot after = timeline.Calculate(Base.AddMinutes(40));

		Assert.Equal(TripStage.NotStarted, before.Stage);
		Assert.Equal(TrackingPhase.Planned, before.Phase);

		Assert.Equal(TripStage.Riding, riding.Stage);
		Assert.Equal(TrackingPhase.InProgress, riding.Phase);
		Assert.Equal("11", riding.MotName);
		Assert.Equal("A", riding.CurrentStop);
		Assert.Equal("M", riding.NextStop);
		Assert.Null(riding.Risk);
		Assert.InRange(riding.Progress, 0.01, 0.5);

		Assert.Equal(TripStage.Arrived, after.Stage);
		Assert.Equal(TrackingPhase.Arrived, after.Phase);
		Assert.Equal(1.0, after.Progress, precision: 6);
	}

	[Fact]
	public void A_delayed_arrival_endangers_a_tight_change()
	{
		// Planned: arrive 08:10, leave 08:15, footpath needs 3 min. Real arrival 08:13:30 leaves 1:30 too little.
		TripTimeline timeline = Parse(Trip(rideOneArrival: 10, secondDeparture: 15, realtimeArrival: 13.5));

		TripSnapshot snapshot = timeline.Calculate(Base.AddMinutes(12));

		Assert.Equal(TrackingPhase.AtRisk, snapshot.Phase);
		Assert.NotNull(snapshot.Risk);
		Assert.False(snapshot.Risk!.Missed);
		Assert.Equal("B", snapshot.Risk.Station);
	}

	[Fact]
	public void A_connection_that_leaves_before_the_arrival_is_missed()
	{
		TripTimeline timeline = Parse(Trip(rideOneArrival: 10, secondDeparture: 15, realtimeArrival: 16));

		TripSnapshot snapshot = timeline.Calculate(Base.AddMinutes(12));

		Assert.Equal(TrackingPhase.AtRisk, snapshot.Phase);
		Assert.True(snapshot.Risk!.Missed);
	}

	[Fact]
	public void A_comfortable_change_is_no_risk()
	{
		TripTimeline timeline = Parse(Trip(rideOneArrival: 10, secondDeparture: 15, realtimeArrival: 11));

		TripSnapshot snapshot = timeline.Calculate(Base.AddMinutes(11.5));

		Assert.Null(snapshot.Risk);
		Assert.Equal(TrackingPhase.AtInterchange, snapshot.Phase);
	}

	[Fact]
	public void Updates_without_polyline_keep_the_known_one()
	{
		string withLine =
			"""
			{"data_version":1,"episodes":[{"type":"public","mot":{"name":"1","direction":"X"},
			"from":{"name":"A","scheduledTime":1000,"coords":{"lat":51.0,"lon":13.7}},
			"to":{"name":"B","scheduledTime":2000,"coords":{"lat":51.1,"lon":13.8}},
			"polyline":[{"lat":51.0,"lon":13.7},{"lat":51.1,"lon":13.8}]}]}
			""";

		string without =
			"""
			{"data_version":2,"episodes":[{"type":"public","mot":{"name":"1","direction":"X"},
			"from":{"name":"A","scheduledTime":1000},"to":{"name":"B","scheduledTime":2000}}]}
			""";

		TripTimeline first = Parse(withLine);

		using JsonDocument document = JsonDocument.Parse(without);

		Assert.True(TripTimeline.TryParse(document.RootElement, first, out TripTimeline second));
		Assert.Equal(2, second.DataVersion);
		Assert.Equal(2, second.Episodes[0].Polyline.Count);
	}

	// ----- Plan list, options, notices, time -----

	[Fact]
	public void Plan_list_is_parsed_from_array_and_wrapper_with_exact_ids()
	{
		const string plan =
			"""
			{"plan_id":"plan-17","active_trip_id":"t-1","deactivated":true,
			 "planOptions":{"type":"static","attentions":{"start":{"active":false,"timeBeforeSeconds":600},"change":false,"problem":true}}}
			""";

		foreach (string json in new[] { $"[{plan}]", $"{{\"plans\":[{plan}]}}" })
		{
			using JsonDocument document = JsonDocument.Parse(json);

			SchutzengelPlanInfo info = Assert.Single(SchutzengelPlanList.Parse(document.RootElement));

			Assert.Equal("plan-17", info.PlanId);
			Assert.Equal("t-1", info.ActiveTripId);
			Assert.True(info.Deactivated);
			Assert.False(info.Options.StartActive);
			Assert.Equal(600, info.Options.StartLeadSeconds);
			Assert.False(info.Options.Change);
			Assert.True(info.Options.Problem);
			Assert.Equal(TimeSpan.Zero, info.Options.StartLead);
		}
	}

	[Fact]
	public void Options_round_trip_through_the_payload()
	{
		SchutzengelOptions updated =
			SchutzengelOptions.Default.With(new WatchOptions(true, 10, false, true));

		string json = JsonSerializer.Serialize(updated.ToPayload());

		using JsonDocument document = JsonDocument.Parse(json);

		SchutzengelOptions parsed = SchutzengelOptions.Parse(document.RootElement);

		Assert.Equal(600, parsed.StartLeadSeconds);
		Assert.False(parsed.Change);
		Assert.Equal(new WatchOptions(true, 10, false, true), parsed.ToWatchOptions());
		Assert.Equal(TimeSpan.FromMinutes(10), parsed.StartLead);
	}

	[Fact]
	public void Notices_are_chronological_and_classified()
	{
		using JsonDocument document =
			JsonDocument.Parse(
				"""
				[{"title":"Info","message":"Anschluss gefährdet","notificationTime":2000},
				 {"title":"Info","message":"Fahrt fällt aus","notificationTime":3000},
				 {"title":"Hinweis","message":"Kleine Verspätung","notificationTime":1000},
				 {"title":"","message":""}]
				""");

		IReadOnlyList<SchutzengelNotice> notices = SchutzengelNotices.Parse(document.RootElement);

		Assert.Equal(3, notices.Count);
		Assert.Equal(SchutzengelNoticeSeverity.Information, notices[0].Severity);
		Assert.Equal(SchutzengelNoticeSeverity.ConnectionRisk, notices[1].Severity);
		Assert.Equal(SchutzengelNoticeSeverity.Cancellation, notices[2].Severity);
	}

	[Fact]
	public void Timestamps_accept_milliseconds_seconds_and_iso()
	{
		DateTimeOffset expected = new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);

		foreach (string json in new[]
		{
			expected.ToUnixTimeMilliseconds().ToString(),
			expected.ToUnixTimeSeconds().ToString(),
			$"\"{expected:O}\""
		})
		{
			using JsonDocument document = JsonDocument.Parse(json);

			Assert.Equal(expected, SchutzengelTime.Read(document.RootElement));
		}
	}

	// ----- Fingerprint -----

	[Fact]
	public void Fingerprint_ignores_walking_and_realtime_and_matches_the_raw_data_summary()
	{
		DateTimeOffset departure = Base.AddHours(1);
		DateTimeOffset arrival = Base.AddHours(2);

		Journey journey = BuildJourney(departure, arrival, realtimeDelay: TimeSpan.FromMinutes(4));
		Journey delayed = BuildJourney(departure, arrival, realtimeDelay: TimeSpan.FromMinutes(9));

		string? key = JourneyFingerprint.Of(journey);

		Assert.NotNull(key);
		Assert.Equal(key, JourneyFingerprint.Of(delayed));
		Assert.Equal(
			JourneyFingerprint.Compose(
				departure.ToUnixTimeMilliseconds(),
				arrival.ToUnixTimeMilliseconds(),
				["11"]),
			key);
	}

	[Fact]
	public void A_journey_without_rides_has_no_fingerprint()
	{
		var journey =
			new Journey
			{
				From = Station("a"),
				To = Station("b"),
				Legs =
				[
					new JourneyLeg
					{
						Mode = TransitMode.Walk,
						From = Station("a"),
						To = Station("b")
					}
				]
			};

		Assert.Null(JourneyFingerprint.Of(journey));
	}

	// ----- Broadcaster -----

	[Fact]
	public async Task Every_subscriber_receives_every_event()
	{
		var broadcaster = new TrackingEventBroadcaster<int>();
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		async Task<List<int>> Collect()
		{
			var items = new List<int>();

			await foreach (int item in broadcaster.SubscribeAsync(cancellation.Token))
			{
				items.Add(item);

				if (items.Count == 2)
				{
					break;
				}
			}

			return items;
		}

		Task<List<int>> first = Collect();
		Task<List<int>> second = Collect();

		// Subscriptions register when enumeration starts.
		await Task.Delay(100, cancellation.Token);

		broadcaster.Publish(1);
		broadcaster.Publish(2);

		Assert.Equal(new[] { 1, 2 }, await first);
		Assert.Equal(new[] { 1, 2 }, await second);
	}

	// ----- Transport -----

	[Fact]
	public async Task Unchanged_realtime_and_empty_notifications_are_visible_as_status_codes()
	{
		var statuses = new Queue<HttpStatusCode>([HttpStatusCode.Created, HttpStatusCode.NoContent]);
		var requests = new List<HttpRequestMessage>();

		var api =
			new SchutzengelApi(
				new HttpClient(new StubHandler(request =>
				{
					requests.Add(request);

					return new HttpResponseMessage(statuses.Dequeue());
				})));

		api.Authenticate("token");

		using SchutzengelResponse realtime = await api.FetchRealtimeAsync("trip", "7", 2, default);
		using SchutzengelResponse notifications = await api.FetchNotificationsAsync("trip", "7", 2, default);

		Assert.Equal(HttpStatusCode.Created, realtime.StatusCode);
		Assert.Equal(HttpStatusCode.NoContent, notifications.StatusCode);

		Assert.All(
			requests,
			request =>
			{
				Assert.Equal("trip", request.Headers.GetValues("trip_id").Single());
				Assert.Equal("7", request.Headers.GetValues("data_version").Single());
				Assert.Equal("2", request.Headers.GetValues("notification_count").Single());
			});
	}

	[Fact]
	public async Task Server_time_is_read_as_a_bare_number()
	{
		var api =
			new SchutzengelApi(
				new HttpClient(new StubHandler(_ =>
					new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent(Base.ToUnixTimeMilliseconds().ToString())
					})));

		api.Authenticate("token");

		using JsonDocument time = await api.GetServerTimeAsync(default);

		TimeSpan? offset = SchutzengelTime.ReadServerOffset(time.RootElement, Base.AddSeconds(-30));

		Assert.Equal(TimeSpan.FromSeconds(30), offset);
	}

	// ----- Helpers -----

	private static TripTimeline Parse(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);

		Assert.True(TripTimeline.TryParse(document.RootElement, null, out TripTimeline timeline));

		return timeline;
	}

	/// <summary>
	/// Ride A→M→B (08:00 – <paramref name="rideOneArrival"/>), a three minute footpath at B,
	/// and ride B→C (<paramref name="secondDeparture"/> – +15 min).
	/// </summary>
	private static string Trip(double rideOneArrival, double secondDeparture, double? realtimeArrival)
	{
		static long Ms(double minutes) => Base.AddMinutes(minutes).ToUnixTimeMilliseconds();

		string realtime = realtimeArrival is { } value ? $",\"realtime\":{Ms(value)}" : string.Empty;

		return
			$$"""
			{"data_version":3,"episodes":[
			 {"type":"public","mot":{"name":"11","direction":"Zschertnitz"},
			  "from":{"name":"A","scheduledTime":{{Ms(0)}} },
			  "to":{"name":"B","scheduledTime":{{Ms(rideOneArrival)}}{{realtime}} },
			  "allStations":[
			   {"name":"A","scheduledTime":{{Ms(0)}} },
			   {"name":"M","scheduledTime":{{Ms(rideOneArrival / 2)}} },
			   {"name":"B","scheduledTime":{{Ms(rideOneArrival)}}{{realtime}} }]},
			 {"type":"individual","durationSeconds":180,
			  "from":{"name":"B","scheduledTime":{{Ms(rideOneArrival)}} },
			  "to":{"name":"B","scheduledTime":{{Ms(secondDeparture)}} } },
			 {"type":"public","mot":{"name":"7","direction":"Pennrich"},
			  "from":{"name":"B","scheduledTime":{{Ms(secondDeparture)}} },
			  "to":{"name":"C","scheduledTime":{{Ms(secondDeparture + 15)}} },
			  "allStations":[
			   {"name":"B","scheduledTime":{{Ms(secondDeparture)}} },
			   {"name":"C","scheduledTime":{{Ms(secondDeparture + 15)}} }]}]}
			""";
	}

	private static Journey BuildJourney(DateTimeOffset departure, DateTimeOffset arrival, TimeSpan realtimeDelay) =>
		new()
		{
			From = Station("a"),
			To = Station("b"),
			Legs =
			[
				new JourneyLeg
				{
					Mode = TransitMode.Walk,
					From = Station("a"),
					To = Station("a2"),
					ScheduledDeparture = departure.AddMinutes(-5),
					ScheduledArrival = departure
				},
				new JourneyLeg
				{
					Mode = TransitMode.Tram,
					From = Station("a2"),
					To = Station("b"),
					ScheduledDeparture = departure,
					RealtimeDeparture = departure + realtimeDelay,
					ScheduledArrival = arrival,
					RealtimeArrival = arrival + realtimeDelay,
					Line = new TransitLine { Name = "11", Mode = TransitMode.Tram }
				}
			]
		};

	private static Station Station(string id) =>
		new()
		{
			Id = id,
			Name = id,
			Place = "Dresden"
		};

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) =>
			Task.FromResult(respond(request));
	}
}