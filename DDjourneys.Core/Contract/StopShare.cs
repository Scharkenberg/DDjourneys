using System.Globalization;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Contract;

/// <summary>
/// Everything a shared stop is made of, independent of the medium: the label under which the passenger
/// knows the stop, the app's own board link for it, and the geo link every phone understands. Composed in
/// one place so the text, the QR code and the links never disagree (the <c>JourneyShareModel</c> discipline).
/// </summary>
/// <param name="Name">The display label: "Postplatz, Dresden" (<see cref="StopLabel.Compose"/>).</param>
/// <param name="AppLink">The board link of the stop, as <see cref="ContractLinks.Departures"/> builds it.</param>
/// <param name="GeoLink">A <c>geo:lat,lon?q=name</c> link (the interchange form of docs/EXTERNAL_CONTRACT.md §8), or null when the stop has no coordinates.</param>
public sealed record StopSharePayload(
	string Name,
	Uri AppLink,
	string? GeoLink)
{
	/// <summary>True when the stop has coordinates: the QR code can be drawn.</summary>
	public bool HasGeoLink => GeoLink is not null;

	/// <summary>
	/// What the QR code carries: the geo link, the one form that works for everyone - a camera app opens a
	/// map at the stop, and DDjourneys itself runs the short "go" form on it. Null when the stop has no
	/// coordinates; the share text still works, only without a code.
	/// </summary>
	public string? QrPayload => GeoLink;
}

/// <summary>
/// Builds the share of a stop from the stop itself. The app link rides on the stop key exactly as
/// <see cref="ContractLinks.Departures"/> composes it (plus the name, so the link still resolves when the
/// recipient's app stands on another provider); the geo link is the honest universal fallback for everyone
/// without the app. A stop without coordinates gets no geo link and no QR code - nothing is invented.
/// </summary>
public static class StopShare
{
	/// <summary>The share of a stop: label, board link, geo link.</summary>
	public static StopSharePayload For(Location stop)
	{
		ArgumentNullException.ThrowIfNull(stop);

		string label = StopLabel.Compose(stop);
		string name = StopLabel.NameFor(stop.Name, stop.Place);

		// The link carries the name without the city (the form of docs/EXTERNAL_CONTRACT.md §8): it is
		// what the app's own resolver searches when the stop key names another provider, and it reads as
		// the plain link it is. A stop without a key falls back to name and coordinates, if it has them.
		var linkPlace = stop.StopKey is { } key
			? new ContractPlace(OrNull(name), key, null, null)
			: new ContractPlace(OrNull(name), null, stop.Latitude, stop.Longitude);

		return new StopSharePayload(label, ContractLinks.Departures(linkPlace), GeoLink(stop));
	}

	/// <summary>
	/// A <c>geo:lat,lon?q=name</c> link for the stop, or null when it has no coordinates. The coordinates
	/// take six decimals (about 10 cm), the query carries the name without the city, URL-encoded - the exact
	/// shape docs/EXTERNAL_CONTRACT.md §8 documents for links any maps app, browser or messenger sends.
	/// </summary>
	public static string? GeoLink(Location stop)
	{
		ArgumentNullException.ThrowIfNull(stop);

		if (stop is not { Latitude: { }, Longitude: { } })
		{
			return null;
		}

		string name = StopLabel.NameFor(stop.Name, stop.Place);
		string position =
			$"{stop.Latitude!.Value.ToString("0.######", CultureInfo.InvariantCulture)},"
			+ $"{stop.Longitude!.Value.ToString("0.######", CultureInfo.InvariantCulture)}";

		return string.IsNullOrWhiteSpace(name)
			? $"geo:{position}"
			: $"geo:{position}?q={Uri.EscapeDataString(name)}";
	}

	private static string? OrNull(string value) =>
		string.IsNullOrWhiteSpace(value) ? null : value;
}
