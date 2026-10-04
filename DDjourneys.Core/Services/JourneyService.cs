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
/// Earlier and later journeys are found with ordinary searches that move a time window
/// (<see cref="JourneyWindow"/>), so they work with every provider; the number of journeys per
/// search is enforced here as well, not left to the providers.
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
	private IEnumerable<IJourneyProvider> _providers =>
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
			in _providers)
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
				await provider.SearchAsync(
					query,
					cancellationToken)
				.ConfigureAwait(false);

			if (result.Outcome == JourneyOutcome.Found)
			{
				// Exactly the requested number when the timetable allows: top up or cut.
				IReadOnlyList<Journey> window =
					await JourneyWindow.FillAsync(
						provider.SearchAsync,
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
	/// journeys on screen, none of them repeated. Works with every provider: the window is moved with
	/// ordinary searches (see <see cref="JourneyWindow"/>), so no provider-side session is needed.
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
			in _providers)
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

			PageResult page =
				await JourneyWindow.PageAsync(
					provider.SearchAsync,
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
					Array.Empty<Journey>()));
		}

		return outcomes.Best()
			?? JourneyResult.Failure(
				"journey_no_providers");
	}


	/// <summary>
	/// Gets journeys preceding the supplied journey.
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

		return PageAsync(
			query,
			[currentJourney],
			previous: true,
			count,
			cancellationToken);
	}


	/// <summary>
	/// Gets journeys following the supplied journey.
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

		return PageAsync(
			query,
			[currentJourney],
			previous: false,
			count,
			cancellationToken);
	}


	/// <summary>
	/// The connection with one leg replaced by an earlier or later alternative, from the provider that
	/// issued the journey. Not suitable when that provider offers no such thing.
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

		IJourneyExtrasProvider? provider =
			_all
				.OfType<IJourneyExtrasProvider>()
				.FirstOrDefault(
					candidate => candidate is IJourneyProvider journeyProvider
						&& (_registry?.IsSelected(journeyProvider) ?? true)
						&& IsSuitable(journeyProvider, journey));

		return provider is null
			? JourneyResult.NotSuitable("no_leg_alternatives")
			: await provider
				.GetLegAlternativeAsync(query, journey, legIndex, previous, cancellationToken)
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
