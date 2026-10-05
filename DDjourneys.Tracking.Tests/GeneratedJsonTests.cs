using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Mapping;
using DDjourneys.Core.Providers.Vvo.Models;
using DDjourneys.Core.Providers.Vvo.Serialization;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Tracking.Tests;

public sealed class GeneratedJsonTests
{
	[Fact]
	public void Vvo_trip_responses_read_through_generated_metadata_with_microsoft_dates()
	{
		const string json =
			"""{"Status":{"Code":"Ok"},"Routes":[{"RouteId":7,"PartialRoutes":[{"RegularStops":[{"Name":"Postplatz","ArrivalTime":"\/Date(1512770460000+0100)\/"}]}]}]}""";

		var info = (JsonTypeInfo<VvoTripResponse>)VvoJson.TypeInfo(typeof(VvoTripResponse))!;

		VvoTripResponse? response = JsonSerializer.Deserialize(json, info);

		VvoStop stop = response!.Routes[0].PartialRoutes[0].RegularStops[0];

		Assert.Equal("Ok", response.Status?.Code);
		Assert.Equal("Postplatz", stop.Name);
		Assert.Equal(1512770460000, stop.ArrivalTime!.Value.ToUnixTimeMilliseconds());
	}

	[Fact]
	public void Provider_dtos_expose_names_and_getters_without_reflection()
	{
		JsonTypeInfo info = VvoJson.TypeInfo(typeof(VvoRoute))!;

		Assert.Contains(info.Properties, property => property.Name == "RouteId" && property.Get is not null);
		Assert.Null(VvoJson.TypeInfo(typeof(DateTime)));
	}

	[Fact]
	public void Implicit_dresden_is_made_explicit_except_for_bare_coordinates()
	{
		Assert.Equal("Dresden", VvoPlaces.Resolve(""));
		Assert.Equal("Dresden", VvoPlaces.Resolve(null, PlaceKind.Stop));
		Assert.Equal("Pirna", VvoPlaces.Resolve(" Pirna ", PlaceKind.Address));
		Assert.Null(VvoPlaces.Resolve(null, PlaceKind.Coordinate));
	}

	[Fact]
	public void Boarding_instructions_age_out_quickly_and_other_notices_do_not()
	{
		DateTimeOffset issued = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

		const string text = "Einstieg Hauptbahnhof: bitte gehen Sie zur Haltestelle Hauptbahnhof Steig 1";

		Assert.True(NoticePolicy.IsBoardingInstruction(text));
		Assert.False(NoticePolicy.IsBoardingInstruction("Umleitung wegen Bauarbeiten"));

		Assert.True(NoticePolicy.Evaluate(NoticeKind.Instruction, issued, false, false, issued.AddMinutes(10)).IsVisible);
		Assert.False(NoticePolicy.Evaluate(NoticeKind.Instruction, issued, false, false, issued.AddMinutes(16)).IsVisible);
		Assert.True(NoticePolicy.Evaluate(NoticeKind.Information, issued, false, false, issued.AddMinutes(16)).IsVisible);
	}
}
