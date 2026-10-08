using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;

namespace DDjourneys.Core.Services;

/// <summary>
/// Provides journey search functionality.
/// </summary>
/// <remarks>
/// This service acts as the application-facing entry point for journey
/// planning. It delegates the actual search to registered journey providers.
///
/// Providers are responsible for:
/// - creating provider-specific requests,
/// - communicating with external APIs,
/// - parsing provider responses,
/// - mapping results into DDjourneys domain models.
///
/// Earlier and later journeys use a provider's native session continuation when available, and
/// fall back to bounded time-window searches for providers without that capability.
///
/// When several providers are eligible they are asked in order. Only an answer with journeys ends
/// the search; an empty answer, a failure and a provider that is not suitable for the request all
/// fall through to the next one. When nobody finds anything, a failure is reported before an empty
/// answer, and an empty answer before "not suitable" (see the private Outcomes helper).
///
/// This keeps the application independent from individual transport APIs.
/// </remarks>
public sealed class JourneyService
{
	private readonly IEnumerable<IJourneyProvider> _all;
	private readonly ProviderRegistry? _registry;


	/// <summary>
	/// Creates a new journey service.
	/// </summary>
	/// <param name="providers">All registered journey providers.</param>
	/// <param name="registry">When given, only the provider the user selected is asked.</param>
	public JourneyService(
		IEnumerable<IJourneyProvider> providers,
		ProviderRegistry? registry = null)
	{
		ArgumentNullException.ThrowIfNull(
			providers);

		_all = providers;
		_registry = registry;
	}


	/// <summary>The providers eligible for the current selection, in registration order.</summary>
	private IEnumerable<IJourneyProvider> Providers =>
		_registry is null
			? _all
			: _all.Where(_registry.IsSelected);


	/// <summary>
	/// Searches for journeys matching the supplied query.
	/// </summary>
	public async Task<JourneyResult> SearchAsync(
		JourneyQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		var outcomes = new Outcomes();

		foreach (IJourneyProvider provider
			in Providers)
		{
			if (!IsSuitable(
				provider,
				query.From,
				query.To))
			{
				outcomes.Add(
					JourneyResult.NotSuitable(
						"journey_endpoint_other_provider"));

				continue;
			}

			JourneyResult result =
				await SearchAsync(
					provider,
					query,
					cancellationToken)
				.ConfigureAwait(false);

			if (result.Outcome == JourneyOutcome.Found)
			{
				// Exactly the requested number when the timetable allows: top up or cut.
				IReadOnlyList<Journey> window =
					await JourneyWindow.FillAsync(
						(next, token) => SearchAsync(provider, next, token),
						query,
						result.Journeys,
						query.MaxResults,
						cancellationToken)
					.ConfigureAwait(false);

				return JourneyResult.Success(
					window);
			}

			outcomes.Add(
				result);
		}

		return outcomes.Best()
			?? JourneyResult.Failure(
				"journey_no_providers");
	}


	/// <summary>
	/// The next <paramref name="count"/> journeys before (<paramref name="previous"/>) or after the
	/// journeys on screen, none of them repeated. Uses a provider's session continuation when available,
	/// with bounded time-window searches (see <see cref="JourneyWindow"/>) as a fallback.
	/// </summary>
	public async Task<JourneyResult> PageAsync(
		JourneyQuery query,
		IReadOnlyList<Journey> shown,
		bool previous,
		int count,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		ArgumentNullException.ThrowIfNull(
			shown);

		var outcomes = new Outcomes();

		foreach (IJourneyProvider provider
			in Providers)
		{
			if (!IsSuitable(
					provider,
					query.From,
					query.To)
				|| shown.Any(
					journey => !IsSuitable(
						provider,
						journey)))
			{
				outcomes.Add(
					JourneyResult.NotSuitable(
						"journey_endpoint_other_provider"));

				continue;
			}

			// A stop-over is applied by this service (see SearchAsync), so the pages are windows of that search, not the
			// provider's own continuation, which knows nothing of journeys that were put together here.
			if (provider is IJourneyContinuationProvider continuation
				&& shown.Count > 0
				&& query.Via is null)
			{
				List<Journey> orderedShown = JourneyWindow.Order(shown, query.SearchMode);
				Journey edge = previous ? orderedShown[0] : orderedShown[^1];
				JourneyResult native = previous
					? await continuation.GetPreviousAsync(query, edge, count, cancellationToken).ConfigureAwait(false)
					: await continuation.GetNextAsync(query, edge, count, cancellationToken).ConfigureAwait(false);

				if (native.Outcome == JourneyOutcome.Found)
				{
					return JourneyResult.Success(JourneyWindow.Order(native.Journeys, query.SearchMode));
				}

				if (native.Outcome == JourneyOutcome.Failed)
				{
					outcomes.Add(native);
				}
			}

			PageResult page =
				await JourneyWindow.PageAsync(
					(next, token) => SearchAsync(provider, next, token),
					query,
					shown,
					previous,
					count,
					cancellationToken)
				.ConfigureAwait(false);

			if (page.Journeys.Count > 0)
			{
				return JourneyResult.Success(
					page.Journeys);
			}

			outcomes.Add(
				page.Failure
				?? JourneyResult.Success(
					[]));
		}

		return outcomes.Best()
			?? JourneyResult.Failure(
				"journey_no_providers");
	}


	/// <summary>
	/// Gets journeys preceding the supplied journey, using its provider's session where available.
	/// </summary>
	public Task<JourneyResult> GetPreviousAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		ArgumentNullException.ThrowIfNull(
			currentJourney);

		return GetAdjacentAsync(query, currentJourney, previous: true, count, cancellationToken);
	}


	/// <summary>
	/// Gets journeys following the supplied journey, using its provider's session where available.
	/// </summary>
	public Task<JourneyResult> GetNextAsync(
		JourneyQuery query,
		Journey currentJourney,
		int count = 5,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(
			query);

		ArgumentNullException.ThrowIfNull(
			currentJourney);

		return GetAdjacentAsync(query, currentJourney, previous: false, count, cancellationToken);
	}

	private async Task<JourneyResult> GetAdjacentAsync(
		JourneyQuery query,
		Journey currentJourney,
		bool previous,
		int count,
		CancellationToken cancellationToken)
	{
		var outcomes = new Outcomes();

		foreach (IJourneyProvider provider in Providers)
		{
			if (query.Via is not null
				|| !IsSuitable(provider, query.From, query.To)
				|| !IsSuitable(provider, currentJourney))
			{
				continue;
			}

			if (provider is not IJourneyContinuationProvider continuation)
			{
				continue;
			}

			JourneyResult result = previous
				? await continuation.GetPreviousAsync(query, currentJourney, count, cancellationToken).ConfigureAwait(false)
				: await continuation.GetNextAsync(query, currentJourney, count, cancellationToken).ConfigureAwait(false);

			if (result.Outcome == JourneyOutcome.Found)
			{
				return result;
			}

			outcomes.Add(result);
		}

		// Use the bounded, provider-independent cursor when native paging is unsupported, has no
		// session, or returned no adjacent journeys. A real provider failure remains visible.
		JourneyResult? nativeFailure = outcomes.Best();
		if (nativeFailure?.Outcome == JourneyOutcome.Failed)
		{
			return nativeFailure;
		}

		return await PageAsync(query, [currentJourney], previous, count, cancellationToken).ConfigureAwait(false);
	}


	/// <summary>
	/// The connection with one ride replaced by the previous or next one. The provider that issued the journey is
	/// asked first (a continuation of its own session); its answer counts only when the ride really moved to the wanted
	/// side. Otherwise, and for providers without such a thing, the journey is rebuilt from ordinary searches (see
	/// <see cref="LegAlternatives"/>). An empty answer means that there is no other ride.
	/// </summary>
	public async Task<JourneyResult> GetLegAlternativeAsync(
		JourneyQuery query,
		Journey journey,
		int legIndex,
		bool previous,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		IJourneyProvider? issuer =
			Providers.FirstOrDefault(
				candidate => IsSuitable(candidate, journey)
					&& IsSuitable(candidate, query.From, query.To));

		if (issuer is null)
		{
			return JourneyResult.NotSuitable("no_leg_alternatives");
		}

		JourneyResult? native = null;

		if (issuer is IJourneyExtrasProvider extras)
		{
			try
			{
				native =
					await extras
						.GetLegAlternativeAsync(query, journey, legIndex, previous, cancellationToken)
						.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				DiagnosticLog.Write($"[Leg alternative] the provider failed: {ex.Message}");
			}

			if (native is { Outcome: JourneyOutcome.Found }
				&& LegAlternatives.Pick(journey, legIndex, previous, native.Journeys) is { } moved)
			{
				return JourneyResult.Success([moved]);
			}

			DiagnosticLog.Write(
				$"[Leg alternative] the provider's answer ({native?.Outcome.ToString() ?? "none"}, {native?.Journeys.Count ?? 0} journey(s)) holds no {(previous ? "earlier" : "later")} ride; searching on");
		}

		Journey? composed =
			await LegAlternatives
				.ComposeAsync(issuer.SearchAsync, query, journey, legIndex, previous, cancellationToken)
				.ConfigureAwait(false);

		if (composed is not null)
		{
			return JourneyResult.Success([composed]);
		}

		return native is { Outcome: JourneyOutcome.Failed }
			? native
			: JourneyResult.Success([]);
	}


	/// <summary>Downloads the printable version of the journey; null when it is not available.</summary>
	public async Task<JourneyDocument?> GetJourneyDocumentAsync(
		JourneyQuery query,
		Journey journey,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		IJourneyExtrasProvider? provider =
			_all
				.OfType<IJourneyExtrasProvider>()
				.FirstOrDefault(
					candidate => candidate is IJourneyProvider journeyProvider
						&& (_registry?.IsSelected(journeyProvider) ?? true)
						&& IsSuitable(journeyProvider, journey));

		return provider is null
			? null
			: await provider
				.GetJourneyDocumentAsync(query, journey, cancellationToken)
				.ConfigureAwait(false);
	}


	/// <summary>Downloads the printable version of the journey; null when it is not available.</summary>
	public async Task<JourneyDocument?> GetJourneyDocumentAsync(
		JourneyQuery query,
		Journey journey,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		IJourneyExtrasProvider? provider =
			_all
				.OfType<IJourneyExtrasProvider>()
				.FirstOrDefault(
					candidate => candidate is IJourneyProvider journeyProvider
						&& (_registry?.IsSelected(journeyProvider) ?? true)
						&& IsSuitable(journeyProvider, journey));

		return provider is null
			? null
			: await provider
				.GetJourneyDocumentAsync(query, journey, cancellationToken)
				.ConfigureAwait(false);
	}


	/// <summary>Address of a printable version of the journey, or null when its provider has none.</summary>
	public Uri? GetJourneyDocumentUri(
		JourneyQuery query,
		Journey journey)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(journey);

		return _all
			.OfType<IJourneyExtrasProvider>()
			.Where(
				candidate => candidate is IJourneyProvider journeyProvider
					&& (_registry?.IsSelected(journeyProvider) ?? true)
					&& IsSuitable(journeyProvider, journey))
			.Select(provider => provider.GetJourneyDocumentUri(query, journey))
			.FirstOrDefault(uri => uri is not null);
	}


	/// <summary>
	/// A provider that declares its id is only asked about places it issued. Places without a provider
	/// id (hand-made or stored before ids existed) are not held against it, and neither are free-form
	/// places without a stop id: the provider itself decides whether it can resolve those.
	/// </summary>
	/// <summary>
	/// A search of one provider that applies the stop-over itself when the query has one: the provider is asked with it,
	/// and only journeys that really pass the place are kept; when there are none (the provider ignores the stop-over,
	/// refuses it or fails on it) the journeys are put together from two searches (see <see cref="ViaRouting"/>).
	/// </summary>
	private static async Task<JourneyResult> SearchAsync(
		IJourneyProvider provider,
		JourneyQuery query,
		CancellationToken cancellationToken)
	{
		if (query.Via is not { } via)
		{
			return await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);
		}

		JourneyResult direct = JourneyResult.NotSuitable("via_not_asked");

		try
		{
			direct = await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Via] the provider failed with the stop-over: {ex.Message}");
		}

		if (direct.Outcome == JourneyOutcome.Found)
		{
			Journey[] passing = [.. direct.Journeys.Where(journey => ViaRouting.Passes(journey, via))];

			if (passing.Length > 0)
			{
				return JourneyResult.Success(passing, direct.Notices);
			}

			DiagnosticLog.Write($"[Via] {direct.Journeys.Count} journey(s) came back, none passes {via.Name}; building them from two searches");
		}
		else
		{
			DiagnosticLog.Write($"[Via] the provider answered {direct.Outcome} ({direct.ErrorMessage}) with the stop-over; building journeys from two searches");
		}

		IReadOnlyList<Journey> built =
			await ViaRouting
				.ComposeAsync(provider.SearchAsync, query, cancellationToken)
				.ConfigureAwait(false);

		if (built.Count > 0)
		{
			return JourneyResult.Success(built);
		}

		// Nothing either way: what the provider said about the stop-over is the answer (a failure stays a failure).
		return direct.Outcome == JourneyOutcome.Found
			? JourneyResult.Success([])
			: direct;
	}


	private static bool IsSuitable(
		IJourneyProvider provider,
		Location from,
		Location to) =>
		provider is not IProviderDescriptor descriptor
		|| IsOwnedBy(descriptor.Info.Id, from)
			&& IsOwnedBy(descriptor.Info.Id, to);


	private static bool IsSuitable(
		IJourneyProvider provider,
		Journey journey) =>
		provider is not IProviderDescriptor descriptor
		|| string.IsNullOrWhiteSpace(journey.ProviderId)
		|| string.Equals(
			descriptor.Info.Id,
			journey.ProviderId,
			StringComparison.OrdinalIgnoreCase);


	private static bool IsOwnedBy(
		string providerId,
		Location location) =>
		!location.IsStation
		|| string.IsNullOrWhiteSpace(location.ProviderId)
		|| string.Equals(
			location.ProviderId,
			providerId,
			StringComparison.OrdinalIgnoreCase);


	/// <summary>
	/// Collects what the providers that did not find anything answered, and picks what to report.
	/// </summary>
	/// <remarks>
	/// Priority: a failure, then an empty answer, then "not suitable".
	/// A failure wins over an empty answer on purpose: if a provider that could have answered did not,
	/// "no journeys" would present an unknown as a fact and the user would not try again.
	/// "Not suitable" comes last because it says nothing about the timetable at all.
	/// </remarks>
	private sealed class Outcomes
	{
		private JourneyResult? _failed;
		private JourneyResult? _empty;
		private JourneyResult? _notSuitable;

		public void Add(
			JourneyResult result)
		{
			switch (result.Outcome)
			{
				case JourneyOutcome.Failed:
					_failed ??= result;
					break;

				case JourneyOutcome.Empty:
					_empty ??= result;
					break;

				case JourneyOutcome.NotSuitable:
					_notSuitable ??= result;
					break;
			}
		}

		public JourneyResult? Best() =>
			_failed
			?? _empty
			?? _notSuitable;
	}
}
