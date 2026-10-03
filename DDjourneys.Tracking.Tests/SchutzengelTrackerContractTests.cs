using System.Net;
using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Tracking;
using DDjourneys.Tracking.Schutzengel;
using DDjourneys.Platforms.Windows;

namespace DDjourneys.Tracking.Tests;

public sealed class SchutzengelTrackerContractTests
{
	[Fact]
	public async Task Account_creation_uses_documented_path_and_returns_plain_token()
	{
		var handler =
			new RecordingHandler(
				_ =>
					new HttpResponseMessage(
						HttpStatusCode.OK)
					{
						Content =
							new StringContent(
								"anonymous-token")
					});


		var api =
			new SchutzengelApi(
				new HttpClient(handler));


		string token =
			await api.CreateAccountAsync(
				CancellationToken.None);


		Assert.Equal(
			"anonymous-token",
			token);


		Assert.Equal(
			"https://m.dvb.de/schutzengel/api/create-account",
			handler.Requests.Single().Uri);


		Assert.Equal(
			HttpMethod.Post,
			handler.Requests.Single().Method);
	}


	[Fact]
	public async Task Authenticated_requests_use_Authentication_bearer_header_and_plan_payload()
	{
		var handler =
			new RecordingHandler(
				_ =>
					new HttpResponseMessage(
						HttpStatusCode.OK)
					{
						Content =
							new StringContent(
								"{\"plan_id\":\"p-42\",\"trip_id\":\"t-42\"}")
					});


		var api =
			new SchutzengelApi(
				new HttpClient(handler));


		api.Authenticate(
			"anonymous-token");


		using var result =
			await api.CreatePlanAsync(
				"{\"legs\":[]}",
				CancellationToken.None);


		Assert.Equal(
			"https://m.dvb.de/schutzengel/plans",
			handler.Requests.Single().Uri);


		Assert.Equal(
			"Bearer anonymous-token",
			handler.Requests.Single().Authentication);


		Assert.Contains(
			"\"legs\":[]",
			handler.Requests.Single().Body);


		Assert.Equal(
			"p-42",
			result.RootElement
				.GetProperty("plan_id")
				.GetString());
	}


	[Fact]
	public void Journey_translation_uses_the_actual_schutzengel_schema()
	{
		DateTimeOffset departure =
			new(
				2026,
				10,
				2,
				8,
				0,
				0,
				TimeSpan.FromHours(2));


		Station from =
			Station(
				"a",
				"Origin",
				51.0400,
				13.7000);


		Station to =
			Station(
				"z",
				"Destination",
				51.0500,
				13.7100);


		var journey =
			new Journey
			{
				Id = "journey-7",

				From =
					from,

				To =
					to,

				Legs =
				[
					new JourneyLeg
					{
						Id = "0",

						Mode =
							TransitMode.Tram,

						From =
							from,

						To =
							to,

						Stops =
						[
							new StopTime
							{
								Station =
									from,

								ScheduledDeparture =
									departure
							},

							new StopTime
							{
								Station =
									to,

								ScheduledArrival =
									departure.AddMinutes(10)
							}
						],

						Path =
						[
							(
								51.0400,
								13.7000),

							(
								51.0450,
								13.7050),

							(
								51.0500,
								13.7100)
						],

						ScheduledDeparture =
							departure,

						ScheduledArrival =
							departure.AddMinutes(10),

						Line =
							new TransitLine
							{
								Name = "3",
								Mode = TransitMode.Tram,
								Destination = "Destination"
							}
					}
				]
			};


		object rawData =
			new
			{
				source = "test"
			};


		using JsonDocument plan =
			JsonDocument.Parse(
				SchutzengelPlanTranslator.Serialize(
					journey,
					rawData));


		JsonElement root =
			plan.RootElement;


		Assert.False(
			root.TryGetProperty(
				"trip_reference",
				out _));


		Assert.Equal(
			"test",
			root.GetProperty(
				"rawData")
				.GetProperty(
					"source")
				.GetString());


		JsonElement episode =
			root
				.GetProperty("journey")
				.GetProperty("episodes")[0];


		Assert.Equal(
			"public",
			episode.GetProperty("type").GetString());


		Assert.Equal(
			"vvo",
			episode.GetProperty("api").GetString());


		Assert.Equal(
			"0",
			episode.GetProperty("id").GetString());


		Assert.Equal(
			"TRAM",
			episode
				.GetProperty("mot")
				.GetProperty("type")
				.GetString());


		Assert.Equal(
			"3",
			episode
				.GetProperty("mot")
				.GetProperty("name")
				.GetString());


		Assert.Equal(
			2,
			episode
				.GetProperty("allStations")
				.GetArrayLength());


		Assert.Equal(
			"a",
			episode
				.GetProperty("from")
				.GetProperty("id")
				.GetString());


		Assert.Equal(
			"z",
			episode
				.GetProperty("to")
				.GetProperty("id")
				.GetString());


		JsonElement polyline =
			episode.GetProperty(
				"polyline");


		Assert.Equal(
			3,
			polyline.GetArrayLength());


		Assert.Equal(
			51.0450,
			polyline[1]
				.GetProperty("lat")
				.GetDouble(),
			precision: 6);


		Assert.Equal(
			13.7050,
			polyline[1]
				.GetProperty("lon")
				.GetDouble(),
			precision: 6);
	}


	[Fact]
	public void Vvo_raw_data_reconstructs_provider_native_connection_shape()
	{
		DateTimeOffset departure =
			new(
				2026,
				10,
				2,
				8,
				0,
				0,
				TimeSpan.Zero);


		Station from =
			Station(
				"a",
				"Origin",
				51.0400,
				13.7000);


		Station to =
			Station(
				"z",
				"Destination",
				51.0500,
				13.7100);


		var journey =
			new Journey
			{
				Id = "0",

				From =
					from,

				To =
					to,

				Legs =
				[
					new JourneyLeg
					{
						Id = "0",

						Mode =
							TransitMode.Tram,

						From =
							from,

						To =
							to,

						Stops =
						[
							new StopTime
							{
								Station =
									from,

								ScheduledDeparture =
									departure
							},

							new StopTime
							{
								Station =
									to,

								ScheduledArrival =
									departure.AddMinutes(10)
							}
						],

						Path =
						[
							(
								51.0400,
								13.7000),

							(
								51.0500,
								13.7100)
						],

						ScheduledDeparture =
							departure,

						ScheduledArrival =
							departure.AddMinutes(10),

						Line =
							new TransitLine
							{
								Name = "3",
								Mode = TransitMode.Tram,
								Destination = "Destination"
							}
					}
				]
			};


		var route =
			new VvoRoute
			{
				RouteId = 12,
				Price = "3,60",
				Net = "voe",
				NumberOfFareZones = "1 Tarifzone",
				FareZoneOrigin = 10,
				FareZoneDestination = 10,
				FareZoneNames = "TZ Dresden (10)",
				Duration = 10,

				PartialRoutes =
				[
					new VvoPartialRoute
					{
						PartialRouteId = 0,
						Duration = 10,

						Mot =
							new VvoMot
							{
								Type = "Tram",
								Name = "3",
								Direction = "Destination"
							},

						RegularStops =
						[
							new VvoStop
							{
								DataId = "a",
								Name = "Origin",
								Place = "Dresden",
								Type = "Stop",
								DepartureTime = departure,
								ArrivalTime = departure,
								Latitude = 4500000,
								Longitude = 5650000
							},

							new VvoStop
							{
								DataId = "z",
								Name = "Destination",
								Place = "Dresden",
								Type = "Stop",
								ArrivalTime = departure.AddMinutes(10),
								DepartureTime = departure.AddMinutes(10),
								Latitude = 4501000,
								Longitude = 5651000
							}
						]
					}
				]
			};


		object raw =
			SchutzengelRawDataTranslator.Translate(
				route,
				journey,
				"session-1",
				new VvoStatus
				{
					Code = "Ok"
				});


		using JsonDocument document =
			JsonDocument.Parse(
				JsonSerializer.Serialize(raw));


		JsonElement root =
			document.RootElement;


		Assert.Equal(
			"12",
			root.GetProperty("id").GetString());


		Assert.Equal(
			360,
			root.GetProperty("price").GetInt32());


		Assert.Equal(
			"voe",
			root.GetProperty("tariffInformation")
				.GetProperty("network")
				.GetString());


		JsonElement partial =
			root
				.GetProperty("partialConnections")[0];


		Assert.Equal(
			1,
			partial
				.GetProperty("mot")
				.GetProperty("type")
				.GetInt32());


		Assert.Equal(
			0,
			partial
				.GetProperty("mot")
				.GetProperty("category")
				.GetInt32());


		JsonElement node =
			partial
				.GetProperty("nodes")[0];


		Assert.Equal(
			51.0400,
			node
				.GetProperty("location")
				.GetProperty("latitude")
				.GetDouble(),
			precision: 6);


		Assert.Equal(
			1,
			node
				.GetProperty("location")
				.GetProperty("projection")
				.GetInt32());


		Assert.Equal(
			"session-1",
			root
				.GetProperty("sessionId")
				.GetString());
	}


	[Fact]
	public async Task Deactivation_and_delete_use_separate_lifecycle_endpoints()
	{
		var handler =
			new RecordingHandler(
				_ =>
					new HttpResponseMessage(
						HttpStatusCode.OK)
					{
						Content =
							new StringContent("{}")
					});


		var api =
			new SchutzengelApi(
				new HttpClient(handler));


		api.Authenticate(
			"token");


		using var paused =
			await api.DeactivateAsync(
				"p-1",
				CancellationToken.None);


		using var deleted =
			await api.DeletePlanAsync(
				"p-1",
				CancellationToken.None);


		using var allDeleted =
			await api.DeleteAllPlansAsync(
				CancellationToken.None);


		Assert.EndsWith(
			"/schutzengel/deactivatePlan",
			handler.Requests[0].Uri);


		Assert.Equal(
			HttpMethod.Delete,
			handler.Requests[1].Method);


		Assert.EndsWith(
			"/schutzengel/plan",
			handler.Requests[1].Uri);

		using JsonDocument deleteBody =
			JsonDocument.Parse(
				handler.Requests[1].Body);

		Assert.Equal(
			"p-1",
			deleteBody
				.RootElement
				.GetProperty("plan_id")
				.GetString());


		Assert.Equal(
			HttpMethod.Delete,
			handler.Requests[2].Method);


		Assert.EndsWith(
			"/schutzengel/allPlans",
			handler.Requests[2].Uri);
	}


	[Fact]
	public async Task Remaining_service_calls_use_the_documented_endpoint_names_and_bearer_header()
	{
		var handler =
			new RecordingHandler(
				_ =>
					new HttpResponseMessage(
						HttpStatusCode.OK)
					{
						Content =
							new StringContent("{}")
					});


		var api =
			new SchutzengelApi(
				new HttpClient(handler));


		api.Authenticate(
			"token-9");


		using var time =
			await api.GetServerTimeAsync(
				default);


		using var all =
			await api.GetAllPlansAsync(
				default);


		using var realtime =
			await api.GetRealtimeAsync(
				"trip 9",
				default);


		using var notifications =
			await api.GetNotificationsAsync(
				"trip 9",
				default);


		using var options =
			await api.SetOptionsAsync(
				"plan-9",
				SchutzengelOptions.Default,
				default);


		using var activated =
			await api.ActivateAsync(
				"plan-9",
				default);


		using var registered =
			await api.RegisterFirebaseAsync(
				"fcm-9",
				default);


		using var unregistered =
			await api.UnregisterFirebaseAsync(
				"fcm-9",
				default);


		Assert.Equal(
			new[]
			{
				"serverTime",
				"plansMinimal",
				"planRealtime?trip_id=trip%209",
				"notifications",
				"planSetOptions",
				"activatePlan",
				"register-firebase",
				"unregister-firebase"
			},
			handler.Requests
				.Select(
					request =>
						new Uri(request.Uri)
							.PathAndQuery
							.Split('/')
							.Last())
				.ToArray());


		Assert.All(
			handler.Requests,
			request =>
				Assert.Equal(
					"Bearer token-9",
					request.Authentication));
	}


	[Fact]
	public async Task Windows_tracker_is_a_noop_and_reports_unavailable()
	{
		var tracker =
			new NoOpJourneyTracker();


		var journey =
			new Journey
			{
				From =
					Station(
						"from",
						"Start"),

				To =
					Station(
						"to",
						"End"),

				Legs =
					Array.Empty<JourneyLeg>()
			};


		Assert.False(
			tracker.IsAvailable);

		Assert.Empty(
			tracker.Watched);

		Assert.Null(
			tracker.Find(journey));

		Assert.False(
			await tracker.CanNotifyAsync());

		await Assert.ThrowsAsync<NotSupportedException>(
			() => tracker.FollowAsync(journey));

		await tracker.RefreshAsync();

		await tracker.DeleteAllAsync();


		await foreach (JourneyTrackingEvent _ in
			tracker.Events)
		{
			Assert.Fail(
				"No-op tracker must not emit updates.");
		}
	}


	private static Station Station(
		string id,
		string name,
		double? latitude = null,
		double? longitude = null)
	{
		return new Station
		{
			Id = id,
			Name = name,
			Place = "Dresden",
			Latitude = latitude,
			Longitude = longitude
		};
	}


	private sealed class RecordingHandler :
		HttpMessageHandler
	{
		public List<CapturedRequest> Requests { get; } =
			[];


		private readonly Func<
			HttpRequestMessage,
			HttpResponseMessage> _responseFactory;


		public RecordingHandler(
			Func<
				HttpRequestMessage,
				HttpResponseMessage> responseFactory)
		{
			_responseFactory =
				responseFactory;
		}


		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			Requests.Add(
				new CapturedRequest(
					request.Method,
					request.RequestUri!.ToString(),
					request.Headers.TryGetValues(
						"Authentication",
						out var values)
						? values.Single()
						: null,
					request.Content is null
						? string.Empty
						: await request.Content.ReadAsStringAsync(
							cancellationToken)));


			return _responseFactory(
				request);
		}
	}


	private sealed record CapturedRequest(
		HttpMethod Method,
		string Uri,
		string? Authentication,
		string Body);
}