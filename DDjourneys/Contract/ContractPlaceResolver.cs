using DDjourneys.Core.Contract;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Services;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Contract;

/// <summary>What the planner should show for a request, with places already resolved.</summary>
/// <param name="When">Provider-zone wall-clock time; null keeps the planner's time.</param>
public sealed record ResolvedPlan(
	Location? From,
	Location? To,
	DateTime? When,
	bool IsNow,
	JourneySearchMode? Mode,
	bool Search,
	Location? Via = null,
	bool AskMissing = false);

/// <summary>
/// Turns the caller's words into places the selected provider understands:
/// a stop key of the selected provider is taken as is, coordinates become a free place, and a name is
/// looked up (an exact station name wins, then the first station, then the first hit). The keywords
/// (<see cref="ContractKeywords"/>) are whatever is current in the app: the stop nearest to the device, the home place,
/// the start the user has set. A keyword that cannot be resolved (no position, no home) leaves the place open, and the
/// planner asks for it instead of the request failing.
/// </summary>
public sealed class ContractPlaceResolver(
	LocationService locations,
	ProviderRegistry providers,
	PlannerLauncher near,
	PlaceStore store,
	AppSettings settings)
{
	private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);

	internal async Task<ResolvedPlan> ResolveAsync(ContractRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		Location? from = await ResolveAsync(request.From, "from", cancellationToken).ConfigureAwait(false);
		Location? to = await ResolveAsync(request.To, "to", cancellationToken).ConfigureAwait(false);
		Location? via = await ResolveAsync(request.Via, "via", cancellationToken).ConfigureAwait(false);

		if (request.Command == ContractCommand.Pick
			&& (from is null || to is null))
		{
			throw new ContractRefusal(
				ContractErrorCode.PlaceNotFound,
				"A place could not be resolved (no device position or no home place set).",
				from is null ? "from" : "to");
		}

		DateTime? when = null;
		bool isNow = false;

		if (request.Time is { } time)
		{
			if (time.IsNow)
			{
				isNow = true;
			}
			else
			{
				when = time.Absolute is { } absolute ? Format.ToWall(absolute) : time.Wall;
			}
		}

		if (when is { } wall)
		{
			DateTime today = Format.NowLocal().Date;

			// The planner's date picker runs from today to a year ahead; the provider knows no more.
			if (wall.Date < today || wall.Date > today.AddYears(1))
			{
				throw new ContractRefusal(
					ContractErrorCode.InvalidParameter,
					"'time' must lie between today and one year ahead (provider time).",
					"time");
			}
		}

		bool complete = from is not null && to is not null;

		// A search that lacks an end opens the planner and asks for it, the way the quick actions do.
		return new ResolvedPlan(
			from,
			to,
			when,
			isNow,
			request.Mode,
			request.Search && complete,
			via,
			AskMissing: request.Search && !complete);
	}

	/// <summary>The place a departures or map request is about; null when it is the device's and no position is known.</summary>
	internal Task<Location?> ResolveAtAsync(ContractRequest request, CancellationToken cancellationToken) =>
		ResolveAsync(request.At, "at", cancellationToken);

	private async Task<Location?> KeywordAsync(string keyword)
	{
		if (string.Equals(keyword, ContractKeywords.Home, StringComparison.OrdinalIgnoreCase))
		{
			return store.Home;
		}

		if (string.Equals(keyword, ContractKeywords.Start, StringComparison.OrdinalIgnoreCase)
			&& settings.StartFrom == StartFromKind.Place
			&& settings.StartFromPlace is { } chosen)
		{
			return chosen;
		}

		// @here, and @start when the user starts where they are.
		return await near.NearMeAsync().ConfigureAwait(false);
	}

	private async Task<Location?> ResolveAsync(ContractPlace? place, string key, CancellationToken cancellationToken)
	{
		if (place is null)
		{
			return null;
		}

		if (ContractKeywords.IsKnown(place.Name)
			&& place.StopKey is null
			&& !place.HasCoordinates)
		{
			return await KeywordAsync(place.Name!).ConfigureAwait(false);
		}

		if (place.StopKey is { } stopKey)
		{
			int colon = stopKey.IndexOf(':');
			string provider = stopKey[..colon];
			string id = stopKey[(colon + 1)..];

			if (string.Equals(provider, providers.SelectedId, StringComparison.OrdinalIgnoreCase))
			{
				return new Location
				{
					Id = id,
					ProviderId = providers.SelectedId,
					Name = place.Name ?? id,
					Latitude = place.Latitude,
					Longitude = place.Longitude
				};
			}

			// Stop ids belong to one provider. Without anything else to go by, refuse rather than guess.
			if (place.Name is null && !place.HasCoordinates)
			{
				throw new ContractRefusal(
					ContractErrorCode.ProviderMismatch,
					$"Stop '{stopKey}' belongs to provider '{provider}', but '{providers.SelectedId}' is selected.",
					$"{key}.stop");
			}
		}

		if (place is { Latitude: { } latitude, Longitude: { } longitude })
		{
			return new Location
			{
				Name =
					place.Name
					?? FormattableString.Invariant($"{latitude:0.#####}, {longitude:0.#####}"),
				Latitude = latitude,
				Longitude = longitude
			};
		}

		if (place.Name is { } name)
		{
			IReadOnlyList<Location> found =
				await locations.SearchAsync(name, LookupTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);

			Location? best =
				found.FirstOrDefault(
					candidate => candidate.IsStation
						&& string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
				?? found.FirstOrDefault(candidate => candidate.IsStation)
				?? (found.Count > 0 ? found[0] : null);

			return best
				?? throw new ContractRefusal(
					ContractErrorCode.PlaceNotFound,
					$"No stop or address found for '{name}'.",
					key);
		}

		throw new ContractRefusal(ContractErrorCode.PlaceNotFound, $"'{key}' names no place.", key);
	}
}
