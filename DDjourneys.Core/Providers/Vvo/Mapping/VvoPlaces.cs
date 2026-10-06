using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>
/// A VVO quirk: places in Dresden often come without a city, as if Dresden were the default. Everywhere the app
/// shows a place it shows the city, so the implicit one is made explicit when the provider's data is mapped.
/// </summary>
public static class VvoPlaces
{
	public const string ImplicitCity = "Dresden";

	/// <summary>The city of a stop, an address or a place of interest: as sent, else Dresden.</summary>
	public static string Resolve(string? place) =>
		string.IsNullOrWhiteSpace(place)
			? ImplicitCity
			: place.Trim();

	/// <summary>
	/// Same, for a searched location. A bare coordinate has no city of its own (it may lie anywhere), so it
	/// keeps whatever the provider said.
	/// </summary>
	public static string? Resolve(string? place, PlaceKind kind) =>
		kind == PlaceKind.Coordinate
			? place
			: Resolve(place);
}
