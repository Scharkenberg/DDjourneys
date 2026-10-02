namespace DDjourneys.Core.Models;

/// <summary>
/// Composes the display label of a stop: the name always followed by its city/region.
/// </summary>
public static class StopLabel
{
	/// <summary>
	/// The city to show next to <paramref name="name"/>, or null when there is none
	/// or the name already ends with it ("Hauptbahnhof, Dresden").
	/// </summary>
	public static string? PlaceFor(string? name, string? place)
	{
		if (string.IsNullOrWhiteSpace(place))
		{
			return null;
		}

		string trimmed = place.Trim();

		return name is not null
			&& name.TrimEnd().EndsWith(
				", " + trimmed,
				StringComparison.OrdinalIgnoreCase)
			? null
			: trimmed;
	}


	/// <summary>"Name, Place", or just the name when no place applies.</summary>
	public static string Compose(string? name, string? place)
	{
		string text = name?.Trim() ?? string.Empty;

		return PlaceFor(text, place) is { } city
			? text.Length == 0
				? city
				: $"{text}, {city}"
			: text;
	}


	public static string Compose(Station station) =>
		Compose(station.Name, station.Place);


	public static string Compose(Location location) =>
		Compose(location.Name, location.Place);


	/// <summary>Parses the provider's platform type; anything unrecognised is <see cref="PlatformKind.Unknown"/>.</summary>
	public static PlatformKind KindOf(string? providerType) =>
		providerType?.Trim().ToLowerInvariant() switch
		{
			"platform" => PlatformKind.Platform,
			"railtrack" => PlatformKind.Railtrack,
			_ => PlatformKind.Unknown
		};
}
