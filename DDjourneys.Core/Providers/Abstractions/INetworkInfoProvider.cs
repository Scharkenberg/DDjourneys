using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>What a transport authority publishes about its network besides timetables.</summary>
public interface INetworkInfoProvider
{
	/// <summary>Route changes and notices; <paramref name="shortTermOnly"/> limits them to short-term disruptions.</summary>
	Task<DisruptionReport> GetDisruptionsAsync(
		bool shortTermOnly = false,
		CancellationToken cancellationToken = default);

	/// <summary>Lines that currently have a route change.</summary>
	Task<IReadOnlyList<DisruptionLine>> GetDisruptedLinesAsync(
		CancellationToken cancellationToken = default);

	/// <summary>Lines that serve a stop, with their directions.</summary>
	Task<IReadOnlyList<StopLine>> GetStopLinesAsync(
		Location stop,
		CancellationToken cancellationToken = default);

	/// <summary>Stops within <paramref name="radiusMeters"/> of a position, nearest first.</summary>
	Task<IReadOnlyList<NearbyStop>> GetNearbyStopsAsync(
		double latitude,
		double longitude,
		int radiusMeters = 500,
		CancellationToken cancellationToken = default);

	/// <summary>The tariff zone a position lies in, or null.</summary>
	Task<TariffZone?> FindTariffZoneAsync(
		double latitude,
		double longitude,
		CancellationToken cancellationToken = default);
}
