using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>Departure monitor: what leaves a stop, and where a vehicle goes.</summary>
public interface IDepartureProvider
{
	Task<DepartureBoard> GetDeparturesAsync(
		DepartureQuery query,
		CancellationToken cancellationToken = default);

	/// <summary>The stops of the run a departure belongs to.</summary>
	Task<IReadOnlyList<RunStop>> GetRunAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// The run with whatever else the provider knows about it (vehicle position, operating days).
	/// Providers with nothing more to say keep this default.
	/// </summary>
	async Task<RunDetail> GetRunDetailAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default) =>
		new(await GetRunAsync(departure, timeoutSeconds, cancellationToken).ConfigureAwait(false));
}
