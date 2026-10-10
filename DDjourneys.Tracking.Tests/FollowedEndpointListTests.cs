using DDjourneys.Core.Models;
using DDjourneys.Core.Storage;

namespace DDjourneys.Tracking.Tests;

/// <summary>The stored endpoints of followed journeys: what a recovery search replans from.</summary>
public sealed class FollowedEndpointListTests
{
	private static readonly Location Home =
		new()
		{
			Id = "33000037",
			Name = "Hauptbahnhof",
			Place = "Dresden",
			Latitude = 51.05,
			Longitude = 13.73,
			Kind = PlaceKind.Stop
		};

	private static readonly Location Work =
		new()
		{
			Id = "33000742",
			Name = "Löbtau",
			Kind = PlaceKind.Stop
		};

	[Fact]
	public void Endpoints_survive_a_round_trip()
	{
		var endpoints =
			new List<FollowedEndpoint>
			{
				new("plan-1", Home, Work),
				new("plan-2", Work, Home, Home)
			};

		IReadOnlyList<FollowedEndpoint> read = FollowedEndpointList.Parse(FollowedEndpointList.Write(endpoints));

		Assert.Equal(2, read.Count);
		Assert.Equal("plan-1", read[0].PlanId);
		Assert.Equal("Hauptbahnhof", read[0].From!.Name);
		Assert.Equal("Löbtau", read[0].To!.Name);
		Assert.Null(read[0].Via);
		Assert.Equal("plan-2", read[1].PlanId);
		Assert.NotNull(read[1].Via);
		Assert.Equal(51.05, read[1].From!.Latitude);
	}

	[Fact]
	public void Absent_ends_read_as_null_and_survive_next_to_present_ones()
	{
		// A journey followed before the store existed has no entry; one with only a start still helps.
		IReadOnlyList<FollowedEndpoint> read = FollowedEndpointList.Parse(
			FollowedEndpointList.Write([new("plan-3", Home, null)]));

		FollowedEndpoint only = Assert.Single(read);
		Assert.Equal("plan-3", only.PlanId);
		Assert.NotNull(only.From);
		Assert.Null(only.To);
		Assert.Null(only.Via);
	}

	[Fact]
	public void Entries_that_do_not_fit_cost_themselves_not_the_list()
	{
		string json =
			"""{"v":2,"items":[{"PlanId":"plan-1","From":{"Name":"Hauptbahnhof","Id":"1","Kind":"Stop"}},{"nothing":"here"},{"PlanId":"plan-2"}]}""";

		IReadOnlyList<FollowedEndpoint> read = FollowedEndpointList.Parse(json);

		Assert.Single(read);
		Assert.Equal("plan-1", read[0].PlanId);
	}

	[Fact]
	public void Forgetting_a_plan_drops_only_its_entry()
	{
		var endpoints =
			new List<FollowedEndpoint>
			{
				new("plan-1", Home, Work),
				new("plan-2", Work, Home)
			};

		IReadOnlyList<FollowedEndpoint> kept = FollowedEndpointList.Without(endpoints, "plan-1");

		FollowedEndpoint only = Assert.Single(kept);
		Assert.Equal("plan-2", only.PlanId);
		Assert.Empty(FollowedEndpointList.Without(kept, "plan-2"));
	Assert.Equal(2, FollowedEndpointList.Without(endpoints, "plan-x").Count);
	}
}
