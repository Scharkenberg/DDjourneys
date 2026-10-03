using DDjourneys.Core.Providers.Vvo.Mapping;
using Xunit;

namespace DDjourneys.Tracking.Tests;

public class VvoStopStatesTests
{
	[Theory]
	[InlineData("Cancelled", true)]
	[InlineData("cancelled", true)]
	[InlineData(" CANCELLED ", true)]
	[InlineData("Delayed", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void IsCancelled_matches_the_provider_state(string? state, bool expected)
	{
		Assert.Equal(expected, VvoStopStates.IsCancelled(state));
	}
}
