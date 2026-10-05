namespace DDjourneys.Core.Models;

/// <summary>What kind of ticket a fare is, so the app can name it in the user's language.</summary>
public enum FareKind
{
	/// <summary>Named by the provider (see <see cref="JourneyFare.Name"/>).</summary>
	Other,

	/// <summary>A single ticket for this journey.</summary>
	Single,

	/// <summary>A day ticket for the zones of this journey.</summary>
	Day
}

/// <summary>Who a ticket is for (the TRIAS passenger categories).</summary>
public enum PassengerCategory
{
	Adult = 0,

	Youth,

	Child,

	Senior,

	Disabled
}

/// <summary>One ticket the provider quotes for a journey.</summary>
public sealed class JourneyFare
{
	/// <summary>The provider's name for the ticket; the app names <see cref="Kind"/> itself when it knows it.</summary>
	public required string Name { get; init; }

	public FareKind Kind { get; init; }

	public decimal? Price { get; init; }

	/// <summary>ISO 4217 code ("EUR").</summary>
	public string Currency { get; init; } = "EUR";

	/// <summary>Zones, validity or other words of the provider.</summary>
	public string? Description { get; init; }

	/// <summary>The fare zones the journey passes ("Dresden, Radebeul").</summary>
	public string? Zones { get; init; }

	/// <summary>Conditions printed with the ticket ("TicketNotes" of VVO).</summary>
	public string? Notes { get; init; }

	/// <summary>Who the ticket is for (TRIAS passenger categories: adult, child, senior ...), joined.</summary>
	public string? ValidFor { get; init; }

	/// <summary>The passenger categories the ticket is for; empty when the provider does not say (its normal price).</summary>
	public IReadOnlyList<PassengerCategory> Passengers { get; init; } = [];

	/// <summary>Where the ticket is sold or explained, when the provider says.</summary>
	public string? Url { get; init; }
}
