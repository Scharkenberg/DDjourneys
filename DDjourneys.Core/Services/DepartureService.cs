using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Core.Providers.Trias;
using DDjourneys.Core.Providers.Vvo;

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

		DepartureBoard? past = await PastBoardAsync(provider, query, cancellationToken).ConfigureAwait(false);

		DepartureBoard board = past ?? await provider.GetDeparturesAsync(query, cancellationToken).ConfigureAwait(false);

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

	/// <summary>
	/// The VVO WebAPI cannot show the past: it answers a time that has gone with "time was expired and corrected to
	/// current time" and the board of now (seen in the log, whatever the form of the time). The EFA server behind it
	/// can - through its TRIAS interface, which has the same stops under their DHIDs. So a board for a time in the
	/// past is asked there (timetable times; the real-time state of a departure that has gone is not kept anyway).
	/// Null: not this case, or TRIAS has nothing to say - the provider's own answer stands.
	/// </summary>
	private async Task<DepartureBoard?> PastBoardAsync(
		IDepartureProvider provider,
		DepartureQuery query,
		CancellationToken cancellationToken)
	{
		if (query.Time is not { } at
			|| at >= DateTimeOffset.Now - TimeSpan.FromMinutes(2)
			|| provider is not IProviderDescriptor { Info.Id: VvoProviderInfo.Id }
			|| TriasProvider() is not { } trias
			|| DhidOf(query.Stop.Id) is not { } dhid)
		{
			return null;
		}

		try
		{
			DepartureBoard board =
				await trias.GetDeparturesAsync(
					new DepartureQuery
					{
						Stop =
							new Location
							{
								Id = dhid,
								ProviderId = TriasProviderInfo.Id,
								Name = query.Stop.Name,
								Place = query.Stop.Place,
								Kind = PlaceKind.Stop
							},
						Time = at,
						IsArrival = query.IsArrival,
						Limit = query.Limit,
						Modes = query.Modes,
						TimeoutSeconds = query.TimeoutSeconds
					},
					cancellationToken)
				.ConfigureAwait(false);

			DiagnosticLog.Write($"[Departures] {query.Stop.Name} at {at:dd.MM. HH:mm} is in the past: the TRIAS server answered {board.Departures.Count} rows");

			return board.Departures.Count > 0 ? board : null;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Departures] the past board via TRIAS failed: {ex.Message}");

			return null;
		}
	}

	private IDepartureProvider? TriasProvider() =>
		_providers.FirstOrDefault(candidate => candidate is IProviderDescriptor { Info.Id: TriasProviderInfo.Id });

	/// <summary>"33000013" is the stop "de:14612:13" (Dresden); a DHID stays as it is, a platform part is dropped.</summary>
	private static string? DhidOf(string? stopId)
	{
		if (string.IsNullOrWhiteSpace(stopId))
		{
			return null;
		}

		string id = stopId.Trim();

		if (id.Length == 8 && id.StartsWith("33", StringComparison.Ordinal) && id.All(char.IsAsciiDigit))
		{
			string number = id[2..].TrimStart('0');

			return number.Length > 0 ? "de:14612:" + number : null;
		}

		string[] parts = id.Split(':');

		return parts.Length >= 3 && parts[0].Equals("de", StringComparison.OrdinalIgnoreCase)
			? string.Join(':', parts.Take(3))
			: null;
	}

	/// <summary>A run belongs to the provider that made the departure (a past board comes from TRIAS).</summary>
	private IDepartureProvider? ProviderOf(Departure departure) =>
		departure.ProviderData?.GetType().FullName?.Contains("Providers.Trias", StringComparison.Ordinal) == true
			? TriasProvider() ?? Provider
			: Provider;

	public Task<IReadOnlyList<RunStop>> GetRunAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default) =>
		ProviderOf(departure) is { } provider
			? provider.GetRunAsync(departure, timeoutSeconds, cancellationToken)
			: Task.FromResult<IReadOnlyList<RunStop>>([]);

	public Task<RunDetail> GetRunDetailAsync(
		Departure departure,
		int timeoutSeconds = 15,
		CancellationToken cancellationToken = default) =>
		ProviderOf(departure) is { } provider
			? provider.GetRunDetailAsync(departure, timeoutSeconds, cancellationToken)
			: Task.FromResult(new RunDetail([]));
}
