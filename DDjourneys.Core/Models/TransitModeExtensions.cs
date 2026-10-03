namespace DDjourneys.Core.Models;

/// <summary>
/// The one definition of "ride" versus "transfer". A ride is a leg on a scheduled public vehicle;
/// walking, taxi and on-demand legs are individual transport and belong to a transfer. Journey
/// counting (<see cref="Journey.TransferCount"/>), the timeline boundaries, the journey identity and
/// the Schutzengel plan translation all use it.
/// </summary>
public static class TransitModeExtensions
{
	public static bool IsRide(this TransitMode mode) =>
		mode is not (TransitMode.Walk or TransitMode.Taxi or TransitMode.OnDemand);
}
