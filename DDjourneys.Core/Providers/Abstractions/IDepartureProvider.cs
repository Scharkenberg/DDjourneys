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
}
