using System.Text.Json;
using DDjourneys.Core.Contract;
using DDjourneys.Core.Models;

namespace DDjourneys.Tracking.Tests;

public sealed class ContractParserTests
{
	private static ContractRequest Valid(string uri)
	{
		ContractParseResult result = ContractParser.ParseUri(uri);

		Assert.True(result.IsValid, result.Failure?.Message);

		return result.Request!;
	}

	private static ContractFailure Refused(string uri)
	{
		ContractParseResult result = ContractParser.ParseUri(uri);

		Assert.False(result.IsValid);

		return result.Failure!;
	}

	[Fact]
	public void Plan_with_names_is_read_and_decoded()
	{
		ContractRequest request =
			Valid("ddjourneys://plan?from=Dresden%20Hbf&to=Semperoper+Dresden&mode=arr&search=1&ref=abc-1");

		Assert.Equal(ContractCommand.Plan, request.Command);
		Assert.Equal("Dresden Hbf", request.From!.Name);
		Assert.Equal("Semperoper Dresden", request.To!.Name);
		Assert.Equal(JourneySearchMode.Arrival, request.Mode);
		Assert.True(request.Search);
		Assert.Equal("abc-1", request.Reference);
	}

	[Fact]
	public void Versioned_form_puts_version_in_the_host()
	{
		ContractRequest request = Valid("ddjourneys://v1/plan?to=Hauptbahnhof");

		Assert.Equal(ContractCommand.Plan, request.Command);
		Assert.Equal(1, request.Version);
	}

	[Fact]
	public void Newer_version_is_refused_but_still_answerable()
	{
		ContractFailure failure =
			Refused("ddjourneys://v9/plan?to=X&x-error=other%3A%2F%2Ffail&ref=r1");

		Assert.Equal(ContractErrorCode.UnsupportedVersion, failure.Code);
		Assert.Equal("r1", failure.Reference);
		Assert.Equal("other://fail", failure.Callbacks!.Error!.OriginalString);
	}

	[Fact]
	public void Unknown_keys_are_ignored_with_a_warning()
	{
		ContractRequest request = Valid("ddjourneys://plan?to=X&colour=red");

		Assert.Contains(request.Warnings, warning => warning.Contains("colour"));
	}

	[Fact]
	public void Duplicate_keys_keep_the_first()
	{
		ContractRequest request = Valid("ddjourneys://plan?to=A&to=B");

		Assert.Equal("A", request.To!.Name);
		Assert.Contains(request.Warnings, warning => warning.Contains("Duplicate"));
	}

	[Fact]
	public void Query_cannot_override_the_command_in_the_path()
	{
		ContractRequest request = Valid("ddjourneys://plan?command=tracked&to=X");

		Assert.Equal(ContractCommand.Plan, request.Command);
	}

	[Theory]
	[InlineData("ddjourneys://nonsense?to=X", ContractErrorCode.UnknownCommand)]
	[InlineData("ddjourneys://plan", ContractErrorCode.MissingParameter)]
	[InlineData("ddjourneys://plan?from=A&search=1", ContractErrorCode.MissingParameter)]
	[InlineData("ddjourneys://plan?to=X&mode=sideways", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://plan?to=X&time=tomorrow", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://plan?to=X&to.lat=51.0", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://plan?to.lat=91&to.lon=10", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://plan?to.stop=nonsense", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://plan?to=X&v=zero", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://plan?to=X&ref=has%20space", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://plan?to=A%00B", ContractErrorCode.InvalidParameter)]
	[InlineData("https://example.org/plan?to=X", ContractErrorCode.Malformed)]
	[InlineData("", ContractErrorCode.Malformed)]
	public void Bad_requests_are_refused_with_the_matching_code(string uri, ContractErrorCode expected) =>
		Assert.Equal(expected, Refused(uri).Code);

	[Fact]
	public void Overlong_values_are_refused()
	{
		string name = new('x', ContractLimits.MaxValueLength + 1);

		Assert.Equal(ContractErrorCode.InvalidParameter, Refused($"ddjourneys://plan?to={name}").Code);
	}

	[Fact]
	public void Too_many_keys_are_refused()
	{
		var bag = new Dictionary<string, string?> { ["command"] = "plan", ["to"] = "X" };

		for (int index = 0; index < ContractLimits.MaxKeys; index++)
		{
			bag[$"k{index}"] = "v";
		}

		Assert.Equal(ContractErrorCode.Malformed, ContractParser.Parse(bag).Failure!.Code);
	}

	[Fact]
	public void Stop_keys_and_coordinates_are_normalized()
	{
		ContractRequest request =
			Valid("ddjourneys://plan?from.stop=VVO:33000028&to.lat=51.05&to.lon=13.74&to=Altmarkt");

		Assert.Equal("vvo:33000028", request.From!.StopKey);
		Assert.Equal(51.05, request.To!.Latitude);
		Assert.Equal(13.74, request.To.Longitude);
		Assert.Equal("Altmarkt", request.To.Name);
	}

	[Fact]
	public void Time_accepts_now_wall_clock_and_offset_forms()
	{
		ContractTime now = Valid("ddjourneys://plan?to=X&time=now").Time!;
		ContractTime wall = Valid("ddjourneys://plan?to=X&time=2026-10-05T08:30").Time!;
		ContractTime absolute = Valid("ddjourneys://plan?to=X&time=2026-10-05T08:30%2B02:00").Time!;
		ContractTime utc = Valid("ddjourneys://plan?to=X&time=2026-10-05T06:30:00Z").Time!;

		Assert.True(now.IsNow);
		Assert.Equal(new DateTime(2026, 10, 5, 8, 30, 0), wall.Wall);
		Assert.Null(wall.Absolute);
		Assert.Equal(new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.FromHours(2)), absolute.Absolute);
		Assert.Equal(absolute.Absolute, utc.Absolute);
	}

	[Fact]
	public void Pick_needs_both_places_and_a_success_callback()
	{
		Assert.Equal(
			ContractErrorCode.MissingParameter,
			Refused("ddjourneys://pick?from=A&to=B").Code);

		Assert.Equal(
			ContractErrorCode.MissingParameter,
			Refused("ddjourneys://pick?to=B&x-success=app%3A%2F%2Fok").Code);

		ContractRequest request =
			Valid("ddjourneys://pick?from=A&to=B&x-success=app%3A%2F%2Fok");

		Assert.True(request.Search);
	}

	[Theory]
	[InlineData("javascript:alert(1)")]
	[InlineData("file:///etc/passwd")]
	[InlineData("content://contacts/people")]
	[InlineData("intent://x#Intent;scheme=a;end")]
	[InlineData("http://example.org/cb")]
	[InlineData("tel:112")]
	[InlineData("sms:112")]
	[InlineData("mailto:a@b.c")]
	[InlineData("ms-settings:network")]
	[InlineData("ddjourneys://plan?to=X")]
	[InlineData("https://user:pw@example.org/cb")]
	[InlineData("not a uri")]
	public void Dangerous_callbacks_are_refused(string callback)
	{
		string uri = $"ddjourneys://pick?from=A&to=B&x-success={Uri.EscapeDataString(callback)}";

		Assert.Equal(ContractErrorCode.InvalidParameter, Refused(uri).Code);
	}

	[Theory]
	[InlineData("https://example.org/cb")]
	[InlineData("myapp://done")]
	[InlineData("ddepartures://v1/back?x=1")]
	public void Https_and_custom_scheme_callbacks_are_accepted(string callback) =>
		Assert.True(ContractCallbackPolicy.TryParse(callback, out _));

	[Fact]
	public void Fingerprint_ignores_the_reference_and_key_order()
	{
		ContractRequest a = Valid("ddjourneys://plan?to=X&from=Y&ref=1");
		ContractRequest b = Valid("ddjourneys://plan?from=Y&to=X&ref=2");

		Assert.Equal(a.Fingerprint, b.Fingerprint);
	}

	[Fact]
	public void Android_extras_use_the_same_vocabulary()
	{
		ContractParseResult result =
			ContractParser.Parse(
				new Dictionary<string, string?>
				{
					["Command"] = "plan",
					["TO"] = "Neustadt",
					["search"] = null
				});

		Assert.True(result.IsValid);
		Assert.Equal("Neustadt", result.Request!.To!.Name);
	}

	[Fact]
	public void Tracked_carries_the_plan()
	{
		ContractRequest request = Valid("ddjourneys://tracked?plan=abc123");

		Assert.Equal("abc123", request.PlanId);
		Assert.Null(Valid("ddjourneys://tracked").PlanId);
	}

	[Fact]
	public void Capabilities_needs_nothing_else()
	{
		Assert.Equal(ContractCommand.Capabilities, Valid("ddjourneys://capabilities").Command);
	}
}

public sealed class ContractLinkTests
{
	[Fact]
	public void Built_plan_links_parse_back_to_the_same_request()
	{
		var from = new ContractPlace("Dresden Hbf", "vvo:33000028", null, null);
		var to = new ContractPlace("Café & Bar, Neustadt", null, 51.0642, 13.7517);
		var at = new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.FromHours(2));

		Uri link = ContractLinks.Plan(from, to, at, JourneySearchMode.Arrival, search: true, reference: "t-1");

		ContractRequest request = ContractParser.ParseUri(link).Request!;

		Assert.Equal(from, request.From);
		Assert.Equal(to, request.To);
		Assert.Equal(at, request.Time!.Absolute);
		Assert.Equal(JourneySearchMode.Arrival, request.Mode);
		Assert.True(request.Search);
		Assert.Equal("t-1", request.Reference);
	}

	[Fact]
	public void Built_pick_links_carry_both_callbacks()
	{
		Uri link =
			ContractLinks.Pick(
				new ContractPlace("A", null, null, null),
				new ContractPlace("B", null, null, null),
				new Uri("myapp://ok?session=7"),
				new Uri("myapp://fail"));

		ContractRequest request = ContractParser.ParseUri(link).Request!;

		Assert.Equal("myapp://ok?session=7", request.Callbacks.Success!.OriginalString);
		Assert.Equal("myapp://fail", request.Callbacks.Error!.OriginalString);
	}

	[Fact]
	public void Built_tracked_and_capabilities_links_parse()
	{
		Assert.Equal("p9", ContractParser.ParseUri(ContractLinks.Tracked("p9")).Request!.PlanId);

		Assert.Equal(
			ContractCommand.Capabilities,
			ContractParser.ParseUri(ContractLinks.Capabilities(new Uri("myapp://caps"))).Request!.Command);
	}
}

public sealed class ContractReplyTests
{
	private static ContractRequest Request(string uri) =>
		ContractParser.ParseUri(uri).Request!;

	[Fact]
	public void Success_goes_to_x_success_and_keeps_the_existing_query_and_fragment()
	{
		Assert.False(
			ContractParser.ParseUri("ddjourneys://pick?from=A&to=B&ref=r%201&x-success=myapp%3A%2F%2Fok").IsValid,
			"a reference with a space is refused");

		ContractRequest valid =
			Request("ddjourneys://pick?from=A&to=B&ref=r1&x-success=" + Uri.EscapeDataString("myapp://ok?session=7#top"));

		Uri? uri = ContractReply.Success(valid, [new("dep", "08:30")]).ToCallbackUri(valid.Callbacks);

		Assert.NotNull(uri);
		Assert.StartsWith("myapp://ok?session=7&contract=1&status=ok&command=pick&ref=r1&dep=08%3A30", uri!.OriginalString);
		Assert.EndsWith("#top", uri.OriginalString);
	}

	[Fact]
	public void Errors_only_go_to_x_error()
	{
		ContractRequest request = Request("ddjourneys://plan?to=X&x-success=myapp%3A%2F%2Fok");

		ContractReply error = ContractReply.Failure(request, ContractErrorCode.PlaceNotFound, "none");

		Assert.Null(error.ToCallbackUri(request.Callbacks));

		ContractRequest both = Request("ddjourneys://plan?to=X&x-success=myapp%3A%2F%2Fok&x-error=myapp%3A%2F%2Ffail");

		Uri? uri = error.ToCallbackUri(both.Callbacks);

		Assert.StartsWith("myapp://fail?contract=1&status=error&command=plan&code=place_not_found", uri!.OriginalString);
	}

	[Fact]
	public void Failures_without_a_parsed_command_still_reply()
	{
		ContractFailure failure =
			ContractParser.ParseUri("ddjourneys://nonsense?x-error=myapp%3A%2F%2Ffail&ref=z").Failure!;

		Uri? uri = ContractReply.Failure(failure).ToCallbackUri(failure.Callbacks!);

		Assert.Contains("code=unknown_command", uri!.OriginalString);
		Assert.Contains("ref=z", uri.OriginalString);
	}

	[Fact]
	public void Oversized_replies_drop_the_document_and_say_so()
	{
		ContractRequest request = Request("ddjourneys://pick?from=A&to=B&x-success=myapp%3A%2F%2Fok");

		ContractReply reply =
			ContractReply.Success(
				request,
				[new("dep", "x"), new("journey", new string('j', ContractLimits.MaxReplyLength))]);

		Uri uri = reply.ToCallbackUri(request.Callbacks, "journey")!;

		Assert.DoesNotContain("journey=", uri.OriginalString);
		Assert.Contains("truncated=1", uri.OriginalString);
		Assert.Contains("dep=x", uri.OriginalString);
	}

	[Fact]
	public void Capabilities_list_every_command()
	{
		string commands = ContractCapabilities.Values("1.2.3").Single(pair => pair.Key == "commands").Value;

		Assert.Equal("plan,pick,tracked,capabilities,go,departures,home,map,disruptions,live", commands);
	}
}

public sealed class JourneyPayloadTests
{
	private static Journey Sample()
	{
		var origin = new Station { Id = "33000028", ProviderId = "vvo", Name = "Hauptbahnhof", Place = "Dresden", Latitude = 51.04, Longitude = 13.73 };
		var middle = new Station { Id = "33000037", ProviderId = "vvo", Name = "Postplatz", Place = "Dresden" };
		var target = new Station { Id = "33000742", ProviderId = "vvo", Name = "Hellerau", Place = "Dresden", Platform = "2" };

		var zone = TimeSpan.FromHours(2);

		return new Journey
		{
			From = origin,
			To = target,
			Legs =
			[
				new JourneyLeg
				{
					Mode = TransitMode.Walk,
					From = origin,
					To = origin,
					ScheduledDeparture = new DateTimeOffset(2026, 10, 5, 8, 25, 0, zone),
					ScheduledArrival = new DateTimeOffset(2026, 10, 5, 8, 30, 0, zone)
				},
				new JourneyLeg
				{
					Mode = TransitMode.Tram,
					From = origin,
					To = middle,
					Line = new TransitLine { Name = "11", Mode = TransitMode.Tram, Destination = "Zschertnitz" },
					ScheduledDeparture = new DateTimeOffset(2026, 10, 5, 8, 30, 0, zone),
					RealtimeDeparture = new DateTimeOffset(2026, 10, 5, 8, 33, 0, zone),
					ScheduledArrival = new DateTimeOffset(2026, 10, 5, 8, 40, 0, zone),
					DeparturePlatform = "3"
				},
				new JourneyLeg
				{
					Mode = TransitMode.Bus,
					From = middle,
					To = target,
					Line = new TransitLine { Name = "A,B", Mode = TransitMode.Bus },
					ScheduledDeparture = new DateTimeOffset(2026, 10, 5, 8, 45, 0, zone),
					ScheduledArrival = new DateTimeOffset(2026, 10, 5, 9, 5, 0, zone)
				}
			]
		};
	}

	[Fact]
	public void Flat_values_carry_the_headline()
	{
		Dictionary<string, string> flat = JourneyPayload.Flat(Sample()).ToDictionary(pair => pair.Key, pair => pair.Value);

		Assert.Equal("Hauptbahnhof", flat["from"]);
		Assert.Equal("vvo:33000028", flat["from.stop"]);
		Assert.Equal("Hellerau", flat["to"]);
		Assert.Equal("vvo:33000742", flat["to.stop"]);
		Assert.Equal("1", flat["transfers"]);
		Assert.Equal("11,A B", flat["lines"]);
		Assert.Equal("0", flat["cancelled"]);
		Assert.StartsWith("2026-10-05T08:", flat["dep"]);
		Assert.EndsWith("+02:00", flat["arr"]);
	}

	[Fact]
	public void Json_is_valid_and_lists_every_leg()
	{
		using JsonDocument document = JsonDocument.Parse(JourneyPayload.Json(Sample()));
		JsonElement root = document.RootElement;

		Assert.Equal(1, root.GetProperty("schema").GetInt32());
		Assert.Equal("Hauptbahnhof", root.GetProperty("from").GetProperty("name").GetString());

		JsonElement legs = root.GetProperty("legs");

		Assert.Equal(3, legs.GetArrayLength());
		Assert.Equal("walk", legs[0].GetProperty("mode").GetString());
		Assert.Equal("tram", legs[1].GetProperty("mode").GetString());
		Assert.Equal("11", legs[1].GetProperty("line").GetString());
		Assert.True(legs[1].TryGetProperty("depLive", out _));
		Assert.False(legs[2].TryGetProperty("depLive", out _));
		Assert.Equal("3", legs[1].GetProperty("depPlatform").GetString());
	}

	[Theory]
	[InlineData(TransitMode.SuburbanRail, "suburban_rail")]
	[InlineData(TransitMode.LongDistanceTrain, "long_distance_train")]
	[InlineData(TransitMode.Bus, "bus")]
	public void Mode_names_are_snake_case(TransitMode mode, string expected) =>
		Assert.Equal(expected, JourneyPayload.ModeName(mode));
}


public sealed class ContractShortFormTests
{
	private static ContractRequest Valid(string uri)
	{
		ContractParseResult result = ContractParser.ParseUri(uri);

		Assert.True(result.IsValid, result.Failure?.Message);

		return result.Request!;
	}

	[Fact]
	public void Go_needs_only_a_destination_and_starts_where_the_user_starts()
	{
		ContractRequest request = Valid("ddjourneys://go?to=Hellerau");

		Assert.Equal(ContractCommand.Go, request.Command);
		Assert.True(request.Search);
		Assert.Equal(ContractKeywords.Start, request.From!.Name);
		Assert.Equal("Hellerau", request.To!.Name);
	}

	[Fact]
	public void Go_without_a_destination_is_refused() =>
		Assert.Equal(
			ContractErrorCode.MissingParameter,
			ContractParser.ParseUri("ddjourneys://go?from=A").Failure!.Code);

	[Fact]
	public void Plan_with_search_and_no_start_uses_the_default_start()
	{
		ContractRequest request = Valid("ddjourneys://plan?to=Hellerau&search=1");

		Assert.Equal(ContractKeywords.Start, request.From!.Name);
	}

	[Fact]
	public void Pick_still_names_both_ends() =>
		Assert.Equal(
			ContractErrorCode.MissingParameter,
			ContractParser.ParseUri("ddjourneys://pick?to=A&x-success=myapp%3A%2F%2Fok").Failure!.Code);

	[Fact]
	public void Keywords_are_places_and_unknown_ones_are_refused()
	{
		ContractRequest request = Valid("ddjourneys://go?from=@here&to=@HOME&via=Postplatz");

		Assert.Equal(ContractKeywords.Here, request.From!.Name);
		Assert.Equal(ContractKeywords.Home, request.To!.Name);
		Assert.Equal("Postplatz", request.Via!.Name);

		Assert.Equal(
			ContractErrorCode.InvalidParameter,
			ContractParser.ParseUri("ddjourneys://go?to=@work").Failure!.Code);
	}

	[Fact]
	public void Departures_default_to_the_stop_near_the_device()
	{
		ContractRequest request = Valid("ddjourneys://departures");

		Assert.Equal(ContractKeywords.Here, request.At!.Name);
		Assert.Null(request.Mode);

		ContractRequest arrivals = Valid("ddjourneys://departures?at.stop=vvo:33000028&mode=arr&time=now");

		Assert.Equal("vvo:33000028", arrivals.At!.StopKey);
		Assert.Equal(JourneySearchMode.Arrival, arrivals.Mode);
		Assert.True(arrivals.Time!.IsNow);
	}

	[Fact]
	public void Page_commands_read_their_parameters()
	{
		Assert.Equal(ContractCommand.Home, Valid("ddjourneys://home").Command);
		Assert.Null(Valid("ddjourneys://map").At);
		Assert.Equal(51.05, Valid("ddjourneys://map?at.lat=51.05&at.lon=13.74").At!.Latitude);
		Assert.Null(Valid("ddjourneys://disruptions").Line);
		Assert.Equal("S1", Valid("ddjourneys://disruptions?line=S1").Line);
		Assert.Equal("3,11", Valid("ddjourneys://live?line=3,11").Line);
	}

	[Theory]
	[InlineData("ddjourneys://live", ContractErrorCode.MissingParameter)]
	[InlineData("ddjourneys://live?line=S1", ContractErrorCode.InvalidParameter)]
	[InlineData("ddjourneys://disruptions?line=%3Cscript%3E", ContractErrorCode.InvalidParameter)]
	public void Bad_line_values_are_refused(string uri, ContractErrorCode expected) =>
		Assert.Equal(expected, ContractParser.ParseUri(uri).Failure!.Code);

	[Fact]
	public void Built_links_parse_back()
	{
		var hellerau = new ContractPlace("Hellerau", null, null, null);
		var stop = new ContractPlace(null, "vvo:33000028", null, null);

		Assert.Equal("Hellerau", Valid(ContractLinks.Go(hellerau).ToString()).To!.Name);
		Assert.Equal(ContractCommand.Home, Valid(ContractLinks.Home().ToString()).Command);
		Assert.Equal("vvo:33000028", Valid(ContractLinks.Departures(stop, arrivals: true).ToString()).At!.StopKey);
		Assert.Equal(ContractCommand.Map, Valid(ContractLinks.Map().ToString()).Command);
		Assert.Equal("S1", Valid(ContractLinks.Disruptions("S1").ToString()).Line);
		Assert.Equal("3", Valid(ContractLinks.Live("3").ToString()).Line);
		Assert.Equal("Postplatz", Valid(ContractLinks.Plan(null, hellerau, via: new ContractPlace("Postplatz", null, null, null)).ToString()).Via!.Name);
	}
}


public sealed class ContractIntentsTests
{
	[Theory]
	[InlineData("51.05,13.73", null, 51.05, 13.73)]
	[InlineData("51.05,13.73;u=35", null, 51.05, 13.73)]
	[InlineData("0,0?q=Hellerau", "Hellerau", null, null)]
	[InlineData("51.05,13.73?q=Hellerau", "Hellerau", 51.05, 13.73)]
	[InlineData("0,0?q=51.05,13.73(Hellerau)", "Hellerau", 51.05, 13.73)]
	[InlineData("0,0?q=Alter%20Markt+Dresden", "Alter Markt Dresden", null, null)]
	public void A_geo_link_becomes_go_to_that_place(string part, string? name, double? lat, double? lon)
	{
		ContractParseResult? result = ContractIntents.FromGeo(part);

		Assert.NotNull(result);
		Assert.True(result.IsValid, result.Failure?.Message);

		ContractRequest request = result.Request!;

		Assert.Equal(ContractCommand.Go, request.Command);
		Assert.Equal(name, request.To!.Name);
		Assert.Equal(lat, request.To.Latitude);
		Assert.Equal(lon, request.To.Longitude);
	}

	[Theory]
	[InlineData("0,0")]
	[InlineData("")]
	[InlineData("0,0?z=12")]
	public void A_geo_link_without_a_place_is_ignored(string part) =>
		Assert.Null(ContractIntents.FromGeo(part));
}

public sealed class ContractCapabilityKeyTests
{
	private static string Value(string key) =>
		ContractCapabilities.Values("1.2.3").Single(pair => pair.Key == key).Value;

	[Fact]
	public void The_versions_are_part_of_the_answer()
	{
		Assert.Equal(ContractVersion.Current.ToString(), Value("contract"));
		Assert.Equal(ContractVersion.Oldest.ToString(), Value("oldest"));
		Assert.Equal(JourneyPayload.SchemaVersion.ToString(), Value("journey.schema"));
		Assert.Equal("1.2.3", Value("app.version"));
	}

	[Fact]
	public void The_app_does_not_offer_itself_for_shared_text() =>
		Assert.Equal("view:ddjourneys,view:geo", Value("android.intents"));

	[Fact]
	public void Every_key_is_unique()
	{
		string[] keys = [.. ContractCapabilities.Values("1").Select(pair => pair.Key)];

		Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
	}
}
