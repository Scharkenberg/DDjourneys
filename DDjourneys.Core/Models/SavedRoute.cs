using DDjourneys.Core.Models;

namespace DDjourneys.Core.Models;

/// <summary>
/// A saved route preset (start, destination, routing preferences, date-time).
/// </summary>
public sealed record SavedRoute(
	string Name,
	Location From,
	Location To,
	RoutingPreferences Routing,
	DateTime? DefaultDateTime = null,
	bool IsDeparture = true)
{
	public string Description =>
		$"{From.Name} \u2192 {To.Name}";

	public JourneyQuery ToQuery() =>
		new JourneyQuery
		{
			From = From,
			To = To,
			DateTime = DefaultDateTime.HasValue
				? Format.ToOffset(DefaultDateTime.Value)
				: Format.Now(),
			SearchMode = IsDeparture
				? JourneySearchMode.Departure
				: JourneySearchMode.Arrival,
			Routing = Routing
		};
}
