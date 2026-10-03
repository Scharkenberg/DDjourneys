using System.Net;
using DDjourneys.Core.Api;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Services;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Tracking.Tests;

/// <summary>Provider ids, error bodies, VVO point geometry and multi-provider fall-through.</summary>
public sealed class CoreFoundationsTests
{
	// ----- Provider-qualified keys -----

	[Fact]
	public void Stop_keys_are_qualified_by_provider()
	{
		Assert.Equal("vvo:123", ProviderKey.Compose("VVO", " 123 "));
		Assert.Null(ProviderKey.Compose("vvo", " "));

		Assert.True(ProviderKey.Same("vvo", "1", "VVO", "1"));
		Assert.False(ProviderKey.Same("vvo", "1", "other", "1"));
		Assert.False(ProviderKey.Same("vvo", null, "vvo", null));
	}

	// ----- ApiClient / ApiException -----

	[Fact]
	public async Task Error_response_body_is_part_of_the_exception_message()
	{
		const string body = "Bad   stop\nid";

		using var client =
			new ApiClient(
				new HttpClient(
					new StubHandler(
						_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
						{
							Content = new StringContent(body)
						})));

		ApiException exception =
			await Assert.ThrowsAsync<ApiException>(
				() => client.GetAsync("https://example.test/trips"));

		Assert.Equal(400, exception.StatusCode);
		Assert.False(exception.IsTransient);
		Assert.Equal(body, exception.ResponseBody);
		Assert.Contains("HTTP 400", exception.Message);
		Assert.Contains("Bad stop id", exception.Message);
		Assert.Equal(exception.Message, exception.Detail);
	}

	[Fact]
	public void A_long_error_body_is_shortened_in_the_message_but_kept_in_full()
	{
		string body = new('x', 1000);

		ApiException exception =
			ApiException.FromResponse(
				500,
				"Internal Server Error",
				body,
				isTransient: true);

		Assert.True(exception.IsTransient);
		Assert.Equal(body, exception.ResponseBody);
		Assert.StartsWith("HTTP 500 Internal Server Error: xxx", exception.Message);
		Assert.EndsWith("…", exception.Message);
		Assert.True(exception.Message.Length < 400);
	}

	[Fact]
	public void A_failure_without_an_answer_has_no_message_but_names_its_cause()
	{
		var exception =
			new ApiException(
				string.Empty,
				null,
				true,
				null,
				new HttpRequestException("connection refused"));

		Assert.Equal(string.Empty, exception.Message);
		Assert.Equal("connection refused", exception.Detail);
	}

	// ----- VVO point geometry -----

	[Fact]
	public void Point_coordinates_are_converted_in_either_field_order()
	{
		Assert.True(VvoCoordinateConverter.TryFromPointFields("5656500", "4621300", out var normal));
		Assert.True(VvoCoordinateConverter.TryFromPointFields("4621300", "5656500", out var swapped));

		// Roughly Dresden Hauptbahnhof (51.04 N, 13.73 E).
		Assert.InRange(normal.Latitude, 50.9, 51.2);
		Assert.InRange(normal.Longitude, 13.5, 14.0);

		Assert.Equal(normal.Latitude, swapped.Latitude, precision: 9);
		Assert.Equal(normal.Longitude, swapped.Longitude, precision: 9);
	}

	[Theory]
	[InlineData("0", "0")]
	[InlineData("", "")]
	[InlineData(null, null)]
	[InlineData("abc", "5656500")]
	[InlineData("5656500", "5656500")]
	public void Unusable_point_coordinates_are_rejected(string? first, string? second)
	{
		Assert.False(VvoCoordinateConverter.TryFromPointFields(first, second, out _));
	}

	[Fact]
	public void A_found_point_keeps_its_provider_and_geometry()
	{
		VvoPoint point = VvoPoint.Parse("33000028||Dresden|Hauptbahnhof|5656500|4621300|0||");

		Location location = VvoLocationProvider.Map(point);

		Assert.Equal("33000028", location.Id);
		Assert.Equal(VvoProviderInfo.Id, location.ProviderId);
		Assert.Equal("vvo:33000028", location.StopKey);
		Assert.Equal("Hauptbahnhof", location.Name);
		Assert.Equal("Dresden", location.Place);
		Assert.NotNull(location.Latitude);
		Assert.NotNull(location.Longitude);
		Assert.InRange(location.Latitude!.Value, 50.9, 51.2);
		Assert.InRange(location.Longitude!.Value, 13.5, 14.0);
	}

	[Fact]
	public void A_point_without_coordinates_has_none()
	{
		VvoPoint point = VvoPoint.Parse("33000028||Dresden|Hauptbahnhof|0|0|0||");

		Location location = VvoLocationProvider.Map(point);

		Assert.Equal("33000028", location.Id);
		Assert.Null(location.Latitude);
		Assert.Null(location.Longitude);
	}

	// ----- JourneyResult / JourneyService -----

	[Fact]
	public void A_successful_result_without_journeys_is_empty_not_failed()
	{
		JourneyResult empty = JourneyResult.Success([]);
		JourneyResult found = JourneyResult.Success([SomeJourney()]);

		Assert.Equal(JourneyOutcome.Empty, empty.Outcome);
		Assert.True(empty.IsSuccessful);

		Assert.Equal(JourneyOutcome.Found, found.Outcome);
		Assert.True(found.IsSuccessful);

		Assert.False(JourneyResult.Failure("x").IsSuccessful);
		Assert.False(JourneyResult.NotSuitable("x").IsSuccessful);
	}

	[Fact]
	public async Task An_empty_answer_falls_through_to_the_next_provider()
	{
		var first = new FakeProvider("alpha", JourneyResult.Success([]));
		var second = new FakeProvider("beta", JourneyResult.Success([SomeJourney()]));

		JourneyResult result = await Search(first, second);

		Assert.Equal(JourneyOutcome.Found, result.Outcome);
		Assert.Equal(1, first.Calls);
		Assert.Equal(1, second.Calls);
	}

	[Fact]
	public async Task A_provider_that_is_not_suitable_does_not_hide_a_real_answer()
	{
		var unsuitable = new FakeProvider("alpha", JourneyResult.NotSuitable("no"));
		var found = new FakeProvider("beta", JourneyResult.Success([SomeJourney()]));

		Assert.Equal(JourneyOutcome.Found, (await Search(unsuitable, found)).Outcome);
		Assert.Equal(JourneyOutcome.Found, (await Search(found, unsuitable)).Outcome);

		var empty = new FakeProvider("gamma", JourneyResult.Success([]));

		Assert.Equal(JourneyOutcome.Empty, (await Search(unsuitable, empty)).Outcome);
	}

	[Fact]
	public async Task A_failure_is_reported_before_an_empty_answer()
	{
		var failed = new FakeProvider("alpha", JourneyResult.Failure("vvo_service_error", "HTTP 500"));
		var empty = new FakeProvider("beta", JourneyResult.Success([]));

		JourneyResult result = await Search(failed, empty);

		Assert.Equal(JourneyOutcome.Failed, result.Outcome);
		Assert.Equal("HTTP 500", result.ErrorDetail);

		Assert.Equal(JourneyOutcome.Failed, (await Search(empty, failed)).Outcome);
	}

	[Fact]
	public async Task When_every_provider_is_empty_the_result_is_an_empty_success()
	{
		JourneyResult result =
			await Search(
				new FakeProvider("alpha", JourneyResult.Success([])),
				new FakeProvider("beta", JourneyResult.Success([])));

		Assert.Equal(JourneyOutcome.Empty, result.Outcome);
		Assert.True(result.IsSuccessful);
	}

	[Fact]
	public async Task A_provider_is_not_asked_about_places_of_another_provider()
	{
		var provider = new FakeProvider("alpha", JourneyResult.Success([SomeJourney()]));

		var service = new JourneyService(new IJourneyProvider[] { provider });

		JourneyResult result =
			await service.SearchAsync(
				new JourneyQuery
				{
					From = Place("1", "alpha"),
					To = Place("2", "beta")
				});

		Assert.Equal(0, provider.Calls);
		Assert.Equal(JourneyOutcome.NotSuitable, result.Outcome);
	}

	[Fact]
	public async Task Places_without_a_provider_id_are_not_held_against_a_provider()
	{
		var provider = new FakeProvider("alpha", JourneyResult.Success([SomeJourney()]));

		var service = new JourneyService(new IJourneyProvider[] { provider });

		JourneyResult result =
			await service.SearchAsync(
				new JourneyQuery
				{
					From = Place("1", string.Empty),
					To = Place("2", "alpha")
				});

		Assert.Equal(1, provider.Calls);
		Assert.Equal(JourneyOutcome.Found, result.Outcome);
	}

	[Fact]
	public async Task Without_providers_the_search_fails()
	{
		var service = new JourneyService(Array.Empty<IJourneyProvider>());

		JourneyResult result =
			await service.SearchAsync(
				new JourneyQuery
				{
					From = Place("1", "alpha"),
					To = Place("2", "alpha")
				});

		Assert.Equal(JourneyOutcome.Failed, result.Outcome);
		Assert.Equal("journey_no_providers", result.ErrorMessage);
	}

	// ----- Helpers -----

	// Endpoints that belong to no provider: every provider is eligible, so the chain itself is tested.
	private static Task<JourneyResult> Search(params IJourneyProvider[] providers) =>
		new JourneyService(providers)
			.SearchAsync(
				new JourneyQuery
				{
					From = Place("1", string.Empty),
					To = Place("2", string.Empty)
				});

	private static Location Place(string id, string providerId) =>
		new()
		{
			Id = id,
			ProviderId = providerId,
			Name = id
		};

	private static Journey SomeJourney() =>
		new()
		{
			From = Stop("a"),
			To = Stop("b"),
			Legs = []
		};

	private static Station Stop(string id) =>
		new()
		{
			Id = id,
			Name = id
		};

	private sealed class FakeProvider(
		string providerId,
		JourneyResult result) :
		IJourneyProvider,
		IProviderDescriptor
	{
		public int Calls { get; private set; }

		public ProviderInfo Info { get; } =
			new(
				providerId,
				providerId,
				providerId,
				"Test",
				"Test",
				ProviderCapabilities.Journeys);

		public Task<JourneyResult> SearchAsync(
			JourneyQuery query,
			CancellationToken cancellationToken = default)
		{
			Calls++;

			return Task.FromResult(result);
		}
	}

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) =>
			Task.FromResult(respond(request));
	}
}
