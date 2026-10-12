using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>Departure monitor, answered by the provider the user selected.</summary>
public sealed class DepartureService
{
	private readonly IEnumerable<IDepartureProvider> _providers;
	private readonly ProviderRegistry? _registry;
	private readonly LineIndex? _lineIndex;

	public DepartureService(
		IEnumerable<IDepartureProvider> providers,
		ProviderRegistry? registry = null,
		LineIndex? lineIndex = null)
	{
		ArgumentNullException.ThrowIfNull(providers);

		_providers = providers;
		_registry = registry;
		_lineIndex = lineIndex;
	}

	private IDepartureProvider? Provider =>
		_registry is null
			? _providers.FirstOrDefault()
			: _providers.FirstOrDefault(_registry.IsSelected);

	public bool IsAvailable =>
		Provider is not null;

	/// <summary>
	/// The board of the query's stop. Every board the app sees also says which lines exist, so it feeds the
	/// line search's index (a side effect, never a request of its own).
	/// </summary>
	public async Task<DepartureBoard> GetDeparturesAsync(
		DepartureQuery query,
		CancellationToken cancellationToken = default)
	{
		if (Provider is not { } provider)
		{
			return DepartureBoard.Empty;
		}

		DepartureBoard board = await provider.GetDeparturesAsync(query, cancellationToken).ConfigureAwait(false);

		if (_lineIndex is not null
			&& provider is IProviderDescriptor described)
		{
			Station seenAt = LineIndex.StopOf(query.Stop);

			foreach (Departure departure in board.Departures)
			{
				_lineIndex.Remember(described.Info.Id, departure.Line, seenAt);
			}
		}

		return board;
	}

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
