namespace DDjourneys.Core.Models;

/// <summary>
/// Composes the display label of a stop: the name always followed by its city/region.
/// </summary>
public static partial class StopLabel
{
	[System.Text.RegularExpressions.GeneratedRegex(@"^(?<name>.*\S)\s*\((?<place>[^\d()]{3,40})\)$")]
	private static partial System.Text.RegularExpressions.Regex TrailingParenthesis();

	/// <summary>
	/// The one presentation of a stop, address or place of interest: the name without the city and the city on its own
	/// (never in parentheses). The city is removed from the start ("Dresden, Hauptbahnhof"), the end
	/// ("Hauptbahnhof, Dresden", "Hauptbahnhof (Dresden)", "Hauptbahnhof Dresden") of the name; without a city, a trailing
	/// "(City)" of the name becomes it.
	/// </summary>
	public static (string Name, string? Place) Split(string? name, string? place)
	{
		string text = name?.Trim() ?? string.Empty;
		string? city = Bare(place);

		if (city is null)
		{
			System.Text.RegularExpressions.Match match = TrailingParenthesis().Match(text);

			if (match.Success)
			{
				return (match.Groups["name"].Value.TrimEnd(',', ' '), match.Groups["place"].Value.Trim());
			}

			return (text, null);
		}

		bool changed = true;

		while (changed)
		{
			changed = false;

			foreach (string suffix in new[] { ", " + city, " (" + city + ")", "(" + city + ")", " - " + city, " " + city })
			{
				if (text.Length > suffix.Length
					&& text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				{
					text = text[..^suffix.Length].TrimEnd(',', ' ', '-');
					changed = true;
				}
			}

			foreach (string prefix in new[] { city + ", ", city + " - ", city + " " })
			{
				if (text.Length > prefix.Length
					&& text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					text = text[prefix.Length..].TrimStart(',', ' ', '-');
					changed = true;
				}
			}
		}

		return (text, city);
	}

	private static string? Bare(string? place)
	{
		string? city = place?.Trim();

		if (city is { Length: > 2 } && city[0] == '(' && city[^1] == ')')
		{
			city = city[1..^1].Trim();
		}

		return string.IsNullOrWhiteSpace(city) ? null : city;
	}

	/// <summary>The city to show below <paramref name="name"/>, or null when there is none.</summary>
	public static string? PlaceFor(string? name, string? place) =>
		Split(name, place).Place;

	/// <summary>The name without the city it repeats.</summary>
	public static string NameFor(string? name, string? place) =>
		Split(name, place).Name;

	/// <summary>"Name, Place", or just the name when no place applies.</summary>
	public static string Compose(string? name, string? place)
	{
		(string text, string? city) = Split(name, place);

		return city is null
			? text
			: text.Length == 0
				? city
				: $"{text}, {city}";
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
