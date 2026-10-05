using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>Departure monitor, answered by the provider the user selected.</summary>
public sealed class DepartureService
{
	private readonly IEnumerable<IDepartureProvider> _providers;
	private readonly ProviderRegistry? _registry;

	public DepartureService(
		IEnumerable<IDepartureProvider> providers,
		ProviderRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_providers = providers;
		_registry = registry;
	}

	private IDepartureProvider? Provider =>
		_registry is null
			? _providers.FirstOrDefault()
			: _providers.FirstOrDefault(_registry.IsSelected);

	public bool IsAvailable =>
		Provider is not null;

	public Task<DepartureBoard> GetDeparturesAsync(
		DepartureQuery query,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetDeparturesAsync(query, cancellationToken)
			: Task.FromResult(DepartureBoard.Empty);

	public Task<IReadOnlyList<RunStop>> GetRunAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetRunAsync(departure, timeoutSeconds, cancellationToken)
			: Task.FromResult<IReadOnlyList<RunStop>>([]);

	public Task<RunDetail> GetRunDetailAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default) =>
		Provider is { } provider
			? provider.GetRunDetailAsync(departure, timeoutSeconds, cancellationToken)
			: Task.FromResult(new RunDetail([]));
}
