using DDjourneys.Core.Models;

namespace DDjourneys.Core.Tracking;

/// <summary>
/// The persisted match key of a journey (see <see cref="JourneyIdentity.Key"/>). Kept as the single
/// string entry point for stored watchlists and the Schutzengel raw-data summary; the rules live
/// in <see cref="JourneyIdentity"/>.
/// </summary>
public static class JourneyFingerprint
{
	public static string? Of(Journey journey) =>
		JourneyIdentity.Of(journey)?.Key;

	public static string? Compose(
		long? departureMilliseconds,
		long? arrivalMilliseconds,
		IEnumerable<string?> lines) =>
		JourneyIdentity.Compose(
			departureMilliseconds,
			arrivalMilliseconds,
			lines);
}
