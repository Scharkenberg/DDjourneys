using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>Live vehicle positions (the provider is chosen by the vehicle source, not by the journey provider).</summary>
public sealed class VehicleService
{
	private readonly ILiveVehicleProvider? _provider;

	public VehicleService(
		IEnumerable<ILiveVehicleProvider> providers)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_provider = providers.FirstOrDefault();
	}

	public bool IsAvailable =>
		_provider is not null;

	public IAsyncEnumerable<LiveVehicle> StreamAsync(
		VehicleFilter filter,
		CancellationToken cancellationToken = default) =>
		_provider is { } provider
			? provider.StreamAsync(filter, cancellationToken)
			: AsyncEnumerable.Empty<LiveVehicle>();
}
