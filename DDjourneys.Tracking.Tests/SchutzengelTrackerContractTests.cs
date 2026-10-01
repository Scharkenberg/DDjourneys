using System.Net;
using System.Text;
using System.Text.Json;
using DDjourneys.Core.Models;
using DDjourneys.Core.Tracking;
using DDjourneys.Platforms.Android.LiveJourney.Schutzengel;
using DDjourneys.Platforms.Windows;

namespace DDjourneys.Tracking.Tests;

public sealed class SchutzengelTrackerContractTests
{
	[Fact]
	public async Task Account_creation_uses_documented_path_and_returns_plain_token()
	{
		var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("anonymous-token") });
		var api = new SchutzengelApi(new HttpClient(handler));

		var token = await api.CreateAccountAsync(CancellationToken.None);

		Assert.Equal("anonymous-token", token);
		Assert.Equal("https://schutzengel.ivi.fraunhofer.de/api/create-account", handler.Requests.Single().Uri);
		Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
	}

	[Fact]
	public async Task Authenticated_requests_use_Authentication_bearer_header_and_plan_payload()
	{
		var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"plan_id\":\"p-42\",\"trip_id\":\"t-42\"}") });
		var api = new SchutzengelApi(new HttpClient(handler));
		api.Authenticate("anonymous-token");

		using var result = await api.CreatePlanAsync("{\"legs\":[]}", CancellationToken.None);

		Assert.Equal("https://schutzengel.ivi.fraunhofer.de/api/plans", handler.Requests.Single().Uri);
		Assert.Equal("Bearer anonymous-token", handler.Requests.Single().Authentication);
		Assert.Contains("\"legs\":[]", handler.Requests.Single().Body);
		Assert.Equal("p-42", result.RootElement.GetProperty("plan_id").GetString());
	}

	[Fact]
	public void Journey_translation_keeps_identity_leg_order_interchanges_times_and_risk_hints()
	{
		var departure = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.FromHours(2));
		var interchange = Station("x", "Central");
		var journey = new Journey
		{
			Id = "journey-7",
			From = Station("a", "Origin"),
			To = Station("z", "Destination"),
			Legs = new[]
			{
				new JourneyLeg { Mode = TransitMode.Tram, From = Station("a", "Origin"), To = interchange, ScheduledDeparture = departure, ScheduledArrival = departure.AddMinutes(10), Line = new TransitLine { Name = "3", Mode = TransitMode.Tram } },
				new JourneyLeg { Mode = TransitMode.RegionalTrain, From = interchange, To = Station("z", "Destination"), ScheduledDeparture = departure.AddMinutes(13), ScheduledArrival = departure.AddMinutes(30), Line = new TransitLine { Name = "RE1", Mode = TransitMode.RegionalTrain } }
			},
			Transfers = new[] { new JourneyTransfer { Location = interchange, PreviousLegIndex = 0, NextLegIndex = 1, Duration = TimeSpan.FromMinutes(3), IsGuaranteed = false } }
		};

		using var plan = JsonDocument.Parse(SchutzengelPlanTranslator.Serialize(journey));
		var root = plan.RootElement;
		Assert.Equal("journey-7", root.GetProperty("client_journey_id").GetString());
		Assert.Equal("3", root.GetProperty("legs")[0].GetProperty("line").GetString());
		Assert.Equal("RE1", root.GetProperty("legs")[1].GetProperty("line").GetString());
		Assert.Equal("x", root.GetProperty("interchanges")[0].GetProperty("location").GetProperty("id").GetString());
		Assert.Equal(1, root.GetProperty("initial_risk_hints").GetProperty("at_risk_transfers").GetInt32());
		Assert.Equal(departure.ToUniversalTime().ToString("O"), root.GetProperty("legs")[0].GetProperty("departure").GetString());
	}

	[Theory]
	[InlineData("{\"notifications\":[{\"message\":\"Missed connection\"}]}", JourneyTrackingEventKind.RiskChanged, TrackingPhase.AtRisk)]
	[InlineData("{\"tripCancelled\":true}", JourneyTrackingEventKind.Cancelled, TrackingPhase.Cancelled)]
	[InlineData("{\"status\":\"arrived\"}", JourneyTrackingEventKind.Arrived, TrackingPhase.Arrived)]
	public void Realtime_and_notification_responses_map_to_contract_events(string response, JourneyTrackingEventKind expectedKind, TrackingPhase expectedPhase)
	{
		var phase = SchutzengelRealtimeTranslator.Translate(response, out var kind, out _);
		Assert.Equal(expectedKind, kind);
		Assert.Equal(expectedPhase, phase);
	}

	[Fact]
	public void Restart_recovery_matches_exact_plan_identifier()
	{
		using var plans = JsonDocument.Parse("{\"plans\":[{\"plan_id\":\"plan-17\"},{\"plan_id\":\"plan-170\"}]}");
		Assert.True(SchutzengelPlanRecovery.ContainsPlan(plans.RootElement, "plan-17"));
		Assert.False(SchutzengelPlanRecovery.ContainsPlan(plans.RootElement, "plan-1"));
	}

	[Fact]
	public async Task Deactivation_and_delete_use_separate_lifecycle_endpoints()
	{
		var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
		var api = new SchutzengelApi(new HttpClient(handler));
		api.Authenticate("token");
		using var paused = await api.DeactivateAsync("p-1", CancellationToken.None);
		using var deleted = await api.DeletePlanAsync("p-1", CancellationToken.None);
		using var allDeleted = await api.DeleteAllPlansAsync(CancellationToken.None);
		Assert.EndsWith("/api/deactivatePlan", handler.Requests[0].Uri);
		Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
		Assert.EndsWith("/api/plan?plan_id=p-1", handler.Requests[1].Uri);
		Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
		Assert.EndsWith("/api/allPlans", handler.Requests[2].Uri);
	}

	[Fact]
	public async Task Remaining_service_calls_use_the_documented_endpoint_names_and_bearer_header()
	{
		var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
		var api = new SchutzengelApi(new HttpClient(handler));
		api.Authenticate("token-9");
		using var time = await api.GetServerTimeAsync(default);
		using var all = await api.GetAllPlansAsync(default);
		using var realtime = await api.GetRealtimeAsync("trip 9", default);
		using var notifications = await api.GetNotificationsAsync("trip 9", default);
		using var options = await api.SetOptionsAsync("plan-9", default);
		using var activated = await api.ActivateAsync("plan-9", default);
		using var registered = await api.RegisterFirebaseAsync("fcm-9", default);
		using var unregistered = await api.UnregisterFirebaseAsync("fcm-9", default);

		Assert.Equal(new[] { "serverTime", "plansMinimal", "planRealtime?trip_id=trip%209", "notifications?trip_id=trip%209", "planSetOptions", "activatePlan", "register-firebase", "unregister-firebase" },
			handler.Requests.Select(r => new Uri(r.Uri).PathAndQuery.Split('/').Last()).ToArray());
		Assert.All(handler.Requests, request => Assert.Equal("Bearer token-9", request.Authentication));
	}

	[Fact]
	public void Journey_snapshot_round_trips_for_process_restart_recovery()
	{
		var journey = new Journey
		{
			Id = "restart-test",
			From = Station("from", "Start"),
			To = Station("to", "End"),
			Legs = new[] { new JourneyLeg { Mode = TransitMode.Tram, From = Station("from", "Start"), To = Station("to", "End"), ScheduledDeparture = DateTimeOffset.UtcNow.AddHours(1), ScheduledArrival = DateTimeOffset.UtcNow.AddHours(2), Line = new TransitLine { Name = "2", Mode = TransitMode.Tram } } }
		};

		var restored = JsonSerializer.Deserialize<Journey>(JsonSerializer.Serialize(journey));
		Assert.NotNull(restored);
		Assert.Equal("restart-test", restored.Id);
		Assert.Equal("2", restored.Legs[0].Line?.Name);
	}

	[Fact]
	public async Task Windows_tracker_is_a_noop_and_reports_unavailable()
	{
		var tracker = new NoOpJourneyTracker();
		var journey = new Journey
		{
			From = Station("from", "Start"),
			To = Station("to", "End"),
			Legs = Array.Empty<JourneyLeg>()
		};

		Assert.False(tracker.IsAvailable);
		await tracker.StartAsync(journey);
		await tracker.StopAsync();
		await foreach (var _ in tracker.Events) Assert.Fail("No-op tracker must not emit updates.");
	}

	private static Station Station(string id, string name) => new() { Id = id, Name = name, Place = "Dresden" };

	private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
	{
		public List<CapturedRequest> Requests { get; } = [];
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Requests.Add(new CapturedRequest(request.Method, request.RequestUri!.ToString(), request.Headers.TryGetValues("Authentication", out var values) ? values.Single() : null, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
			return responseFactory(request);
		}
	}

	private sealed record CapturedRequest(HttpMethod Method, string Uri, string? Authentication, string Body);
}

