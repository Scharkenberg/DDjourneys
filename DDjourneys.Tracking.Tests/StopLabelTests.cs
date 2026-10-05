using DDjourneys.Core.Models;

namespace DDjourneys.Tracking.Tests;

/// <summary>One presentation of a stop: the name without its city, the city on its own.</summary>
public sealed class StopLabelTests
{
	[Theory]
	[InlineData("Hauptbahnhof, Dresden", "Dresden", "Hauptbahnhof")]
	[InlineData("Hauptbahnhof (Dresden)", "Dresden", "Hauptbahnhof")]
	[InlineData("Hauptbahnhof Dresden", "Dresden", "Hauptbahnhof")]
	[InlineData("Dresden, Hauptbahnhof", "Dresden", "Hauptbahnhof")]
	[InlineData("Dresden Hauptbahnhof", "dresden", "Hauptbahnhof")]
	[InlineData("Postplatz", "(Dresden)", "Postplatz")]
	[InlineData("Dresden", "Dresden", "Dresden")]
	[InlineData("Dresdner Straße", "Dresden", "Dresdner Straße")]
	public void The_city_is_never_repeated_in_the_name(string name, string place, string expected)
	{
		(string shown, string? city) = StopLabel.Split(name, place);

		Assert.Equal(expected, shown);
		Assert.Equal(place.Trim('(', ')'), city, ignoreCase: true);
	}

	[Fact]
	public void Without_a_city_a_trailing_parenthesis_becomes_it()
	{
		(string name, string? city) = StopLabel.Split("Nöthnitzer Straße 46 (Dresden)", null);

		Assert.Equal("Nöthnitzer Straße 46", name);
		Assert.Equal("Dresden", city);
	}

	[Fact]
	public void A_parenthesis_with_digits_stays_in_the_name()
	{
		(string name, string? city) = StopLabel.Split("Straße (2)", null);

		Assert.Equal("Straße (2)", name);
		Assert.Null(city);
	}
}
