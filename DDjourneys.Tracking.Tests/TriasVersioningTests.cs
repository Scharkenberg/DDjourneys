using System.Net;
using System.Text;
using System.Xml.Linq;
using DDjourneys.Core.Api;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Trias;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Tracking.Tests;

/// <summary>TRIAS version fallback (1.4 to 1.1), request element order and the new response readers.</summary>
public sealed class TriasVersioningTests
{
	private const string Rejected =
		"<Trias><ServiceDelivery><DeliveryPayload><TripResponse>"
		+ "<ErrorMessage><Code>UNKNOWN_ELEMENT</Code><Text><Text>not understood</Text></Text></ErrorMessage>"
		+ "</TripResponse></DeliveryPayload></ServiceDelivery></Trias>";

	private const string Accepted =
		"<Trias><ServiceDelivery><DeliveryPayload><TripResponse>"
		+ "<TripResult><ResultId>1</ResultId></TripResult>"
		+ "</TripResponse></DeliveryPayload></ServiceDelivery></Trias>";

	private static string VersionOf(string body) =>
		XDocument.Parse(body).Root!.Attribute("version")!.Value;

	private static TriasClient ClientAccepting(string acceptedVersion, List<string> seen) =>
		new(
			new ApiClient(
				new HttpClient(
					new StubHandler(
						(request, body) =>
						{
							string version = VersionOf(body);

							seen.Add(version);

							return Respond(version == acceptedVersion ? Accepted : Rejected);
						}))));

	private static HttpResponseMessage Respond(string xml) =>
		new(HttpStatusCode.OK)
		{
			Content = new StringContent(xml, Encoding.UTF8, "text/xml")
		};

	private static Location Stop(string id, string name) =>
		new()
		{
			Id = id,
			Name = name,
			Kind = PlaceKind.Stop
		};

	private static XDocument Trip(TriasDialect dialect, MaxTransfers transfers = MaxTransfers.Two) =>
		TriasRequests.Trip(
			new JourneyQuery
			{
				From = Stop("de:14612:28", "Hauptbahnhof"),
				To = Stop("de:14612:5", "Postplatz"),
				DateTime = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero),
				Routing = new RoutingPreferences { MaxTransfers = transfers }
			},
			dialect);

	[Fact]
	public async Task A_rejected_request_steps_down_one_version_at_a_time()
	{
		var seen = new List<string>();
		TriasClient client = ClientAccepting("1.2", seen);

		XDocument response =
			await client.SendAsync(
				TriasRequestKind.Trip,
				dialect => Trip(dialect),
				CancellationToken.None);

		Assert.Equal(["1.4", "1.3", "1.2"], seen);
		Assert.NotEmpty(response.Descendants().Where(element => element.Name.LocalName == "TripResult"));
	}

	[Fact]
	public async Task The_working_version_is_remembered_per_kind_of_request()
	{
		var seen = new List<string>();
		TriasClient client = ClientAccepting("1.3", seen);

		await client.SendAsync(TriasRequestKind.Trip, dialect => Trip(dialect), CancellationToken.None);
		seen.Clear();
		await client.SendAsync(TriasRequestKind.Trip, dialect => Trip(dialect), CancellationToken.None);

		Assert.Equal(["1.3"], seen);
		Assert.Equal(3, client.Current(TriasRequestKind.Trip).Minor);
		Assert.Equal(4, client.Current(TriasRequestKind.StopEvent).Minor);
	}

	[Fact]
	public async Task A_no_result_answer_is_not_a_rejection()
	{
		var seen = new List<string>();

		var client =
			new TriasClient(
				new ApiClient(
					new HttpClient(
						new StubHandler(
							(request, body) =>
							{
								seen.Add(VersionOf(body));

								return Respond(Rejected.Replace("UNKNOWN_ELEMENT", "TRIP_NOTRIPFOUND"));
							}))));

		await client.SendAsync(TriasRequestKind.Trip, dialect => Trip(dialect), CancellationToken.None);

		Assert.Equal(["1.4"], seen);
	}

	[Fact]
	public async Task When_every_version_is_rejected_the_last_error_answer_is_returned()
	{
		var seen = new List<string>();
		TriasClient client = ClientAccepting("none", seen);

		XDocument response =
			await client.SendAsync(TriasRequestKind.Trip, dialect => Trip(dialect), CancellationToken.None);

		Assert.Equal(["1.4", "1.3", "1.2", "1.1"], seen);
		Assert.Equal("UNKNOWN_ELEMENT", TriasMapper.Error(response)?.Code);
	}

	[Fact]
	public void Trip_parameters_follow_the_schema_sequence()
	{
		XElement parameters =
			Trip(TriasDialect.Latest)
				.Descendants()
				.First(element => element.Name.LocalName == "Params");

		string[] names = [.. parameters.Elements().Select(element => element.Name.LocalName)];

		Assert.DoesNotContain("TransferLimit", names);

		string[] ordered =
			["WalkSpeed", "NumberOfResults", "InterchangeLimit", "AlgorithmType", "IncludeTrackSections", "IncludeSituationInfo", "IncludeIntermediateStops", "IncludeFares"];

		int[] positions = [.. ordered.Select(name => Array.IndexOf(names, name))];

		Assert.DoesNotContain(-1, positions);
		Assert.Equal(positions.Order(), positions);
	}

	[Fact]
	public void Older_versions_drop_the_newer_content_flags()
	{
		string[] names =
			[.. Trip(new TriasDialect(2))
				.Descendants()
				.First(element => element.Name.LocalName == "Params")
				.Elements()
				.Select(element => element.Name.LocalName)];

		Assert.DoesNotContain("IncludeSituationInfo", names);
		Assert.DoesNotContain("IncludeEstimatedTimes", names);
		Assert.Contains("IncludeFares", names);
	}

	[Fact]
	public void The_envelope_carries_the_dialect_version()
	{
		Assert.Equal("1.3", VersionOf(Trip(new TriasDialect(3)).ToString()));
	}

	[Fact]
	public void No_transfers_is_not_sent_as_a_limit()
	{
		bool hasLimit =
			Trip(TriasDialect.Latest, MaxTransfers.None)
				.Descendants()
				.Any(element => element.Name.LocalName == "InterchangeLimit");

		Assert.False(hasLimit);
	}

	[Fact]
	public void Trip_info_asks_for_calls_estimates_and_position()
	{
		XDocument request = TriasRequests.TripInfo("ref:1", "2026-10-05", TriasDialect.Latest);

		string[] names =
			[.. request
				.Descendants()
				.First(element => element.Name.LocalName == "Params")
				.Elements()
				.Select(element => element.Name.LocalName)];

		Assert.Equal(
			["IncludeCalls", "IncludeEstimatedTimes", "IncludePosition", "IncludeService", "IncludeSituationInfo"],
			names);
	}

	[Fact]
	public void Trip_info_calls_become_a_run_with_the_current_stop_marked()
	{
		XDocument response =
			XDocument.Parse(
				"<Trias><TripInfoResult>"
				+ "<PreviousCall><CallAtStop><StopPointRef>de:14612:1:1</StopPointRef><StopPointName><Text>A</Text></StopPointName></CallAtStop></PreviousCall>"
				+ "<CurrentPosition><GeoPosition><Longitude>13.7</Longitude><Latitude>51.0</Latitude></GeoPosition></CurrentPosition>"
				+ "<OnwardCall><CallAtStop><StopPointRef>de:14612:2:1</StopPointRef><StopPointName><Text>B</Text></StopPointName></CallAtStop></OnwardCall>"
				+ "<OnwardCall><CallAtStop><StopPointRef>de:14612:3:1</StopPointRef><StopPointName><Text>C</Text></StopPointName></CallAtStop></OnwardCall>"
				+ "</TripInfoResult></Trias>");

		RunDetail run = TriasMapper.MapRun(response, "de:14612:2");

		Assert.Equal(
			[RunPosition.Previous, RunPosition.Current, RunPosition.Onward],
			run.Stops.Select(stop => stop.Position));
		Assert.Equal(51.0, run.Vehicle?.Latitude);
		Assert.Equal(13.7, run.Vehicle?.Longitude);
	}

	[Fact]
	public void Referenced_situations_become_leg_and_journey_notices()
	{
		XDocument response =
			XDocument.Parse(
				"<Trias><TripResponse>"
				+ "<TripResponseContext><Situations><PtSituation>"
				+ "<SituationNumber>S1</SituationNumber><Summary>Line 3 diverted</Summary><Description>Because of works</Description>"
				+ "</PtSituation></Situations></TripResponseContext>"
				+ "<TripResult><ResultId>r</ResultId><Trip><TripId>t</TripId>"
				+ "<TripLeg><LegId>1</LegId><TimedLeg><Service><SituationFullRef><SituationNumber>S1</SituationNumber></SituationFullRef></Service></TimedLeg></TripLeg>"
				+ "<SituationFullRef><SituationNumber>S1</SituationNumber></SituationFullRef>"
				+ "</Trip></TripResult>"
				+ "</TripResponse></Trias>");

		Journey journey =
			Assert.Single(
				TriasMapper.MapJourneys(response, Stop("de:14612:28", "A"), Stop("de:14612:5", "B")));

		Assert.Contains("Line 3 diverted", journey.Notices);
		Assert.Contains("Line 3 diverted", journey.Legs[0].Notices);
	}

	private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
	{
		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			string body =
				request.Content is null
					? string.Empty
					: await request.Content.ReadAsStringAsync(cancellationToken);

			return respond(request, body);
		}
	}
}
