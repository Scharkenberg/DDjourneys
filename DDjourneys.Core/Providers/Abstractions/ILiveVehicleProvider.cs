using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>Streams live vehicle positions.</summary>
public interface ILiveVehicleProvider
{
	/// <summary>
	/// Positions as they arrive, until <paramref name="cancellationToken"/> is cancelled or the
	/// connection ends.
	/// </summary>
	IAsyncEnumerable<LiveVehicle> StreamAsync(
		VehicleFilter filter,
		CancellationToken cancellationToken = default);
}
