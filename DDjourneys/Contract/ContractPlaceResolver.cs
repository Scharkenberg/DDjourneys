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
	bool Search);

/// <summary>
/// Turns the caller's words into places the selected provider understands:
/// a stop key of the selected provider is taken as is, coordinates become a free place, and a name is
/// looked up (an exact station name wins, then the first station, then the first hit).
/// </summary>
public sealed class ContractPlaceResolver(LocationService locations, ProviderRegistry providers)
{
	private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);

	internal async Task<ResolvedPlan> ResolveAsync(ContractRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		Location? from = await ResolveAsync(request.From, "from", cancellationToken).ConfigureAwait(false);
		Location? to = await ResolveAsync(request.To, "to", cancellationToken).ConfigureAwait(false);

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

		return new ResolvedPlan(from, to, when, isNow, request.Mode, request.Search);
	}

	private async Task<Location?> ResolveAsync(ContractPlace? place, string key, CancellationToken cancellationToken)
	{
		if (place is null)
		{
			return null;
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
				await locations.SearchAsync(name, cancellationToken, LookupTimeout).ConfigureAwait(false);

			Location? best =
				found.FirstOrDefault(
					candidate => candidate.IsStation
						&& string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
				?? found.FirstOrDefault(candidate => candidate.IsStation)
				?? found.FirstOrDefault();

			return best
				?? throw new ContractRefusal(
					ContractErrorCode.PlaceNotFound,
					$"No stop or address found for '{name}'.",
					key);
		}

		throw new ContractRefusal(ContractErrorCode.PlaceNotFound, $"'{key}' names no place.", key);
	}
}
