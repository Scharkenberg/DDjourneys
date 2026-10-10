using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Vvo;

namespace DDjourneys.Core.Services;

/// <summary>
/// A provider that quotes no tickets (the VVO's TRIAS interface answers journeys without a fare) can still
/// show what the journey costs: the VVO WebAPI quotes a price for the very same ride. The same connection is
/// searched there (stops by their names, the time of the first ride), recognised by its sequence of lines and
/// the planned start of its first ride, and its tickets are shown with the journey. Only a borrowed answer
/// that matches is used - never a price for a different connection - and nothing is borrowed from the
/// provider the journey came from, or when the journey already carries fares.
/// </summary>
public sealed class FareBorrower(
	JourneyService journeys,
	LocationService locations,
	ProviderRegistry providers)
{
	/// <summary>The planned start of the first ride may differ by this much between the interfaces.</summary>
	private static readonly TimeSpan StartTolerance = TimeSpan.FromMinutes(2);

	/// <summary>Whether looking for fares elsewhere is worth a request.</summary>
	public bool Wants(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		return journey.Fares.Count == 0
			&& journey.Legs.Any(leg => leg.IsRide)
			&& providers.Find(VvoProviderInfo.Id) is not null
			&& !string.Equals(providers.SelectedId, VvoProviderInfo.Id, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The tickets of the same connection from the VVO WebAPI; empty when it cannot be told or found.</summary>
	public async Task<IReadOnlyList<JourneyFare>> BorrowAsync(
		Journey journey,
		JourneyQuery? asked = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(journey);

		if (!Wants(journey)
			|| journey.Legs.FirstOrDefault(leg => leg.IsRide)?.ScheduledDeparture is not { } start)
		{
			return [];
		}

		try
		{
			// Everything below belongs to the donor provider, whatever the app shows.
			using IDisposable scope = providers.Override(VvoProviderInfo.Id);

			Location? from = await ResolveAsync(asked?.From ?? Of(journey.Origin ?? journey.From), cancellationToken).ConfigureAwait(false);
			Location? to = await ResolveAsync(asked?.To ?? Of(journey.Destination ?? journey.To), cancellationToken).ConfigureAwait(false);
			Location? via = asked?.Via is { } stopOver ? await ResolveAsync(stopOver, cancellationToken).ConfigureAwait(false) : null;

			if (from is null || to is null || (asked?.Via is not null && via is null))
			{
				DiagnosticLog.Write("[Fares] no ticket prices borrowed: a place is unknown to the VVO WebAPI");

				return [];
			}

			JourneyResult result =
				await journeys.SearchAsync(
					new JourneyQuery
					{
						From = from,
						To = to,
						Via = via,
						DateTime = start - TimeSpan.FromMinutes(3),
						MaxResults = 8
					},
					cancellationToken)
				.ConfigureAwait(false);

			string[] wanted = Rides(journey);

			Journey? same =
				result.Journeys.FirstOrDefault(
					candidate =>
						candidate.Fares.Count > 0
						&& Rides(candidate).SequenceEqual(wanted, StringComparer.Ordinal)
						&& candidate.Legs.FirstOrDefault(leg => leg.IsRide)?.ScheduledDeparture is { } other
						&& (other - start).Duration() <= StartTolerance);

			if (same is null)
			{
				// The price depends on the relation (zones), not on the single trip: when every candidate
				// quotes the same tickets, they are the tickets of this relation too.
				List<Journey> priced = [.. result.Journeys.Where(candidate => candidate.Fares.Count > 0)];

				if (priced.Count > 0
					&& priced.TrueForAll(candidate => Prices(candidate) == Prices(priced[0])))
				{
					DiagnosticLog.Write($"[Fares] {priced[0].Fares.Count} ticket prices borrowed from the VVO WebAPI by relation ({from.Name} > {to.Name}{(via is null ? string.Empty : " via " + via.Name)})");

					return priced[0].Fares;
				}
			}

			DiagnosticLog.Write(
				same is null
					? $"[Fares] no ticket prices borrowed: none of {result.Journeys.Count} journeys is the same connection ({string.Join(">", wanted)} at {start:HH:mm})"
					: $"[Fares] {same.Fares.Count} ticket prices borrowed from the VVO WebAPI for {string.Join(">", wanted)} at {start:HH:mm}");

			return same?.Fares ?? [];
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Fares] borrowing ticket prices failed: {ex.Message}");

			return [];
		}
	}

	private async Task<Location?> ResolveAsync(Location source, CancellationToken cancellationToken)
	{
		bool station = source.IsStation;

		IReadOnlyList<Location> found =
			await locations.SearchAsync(
				source.Name,
				kinds: station ? PlaceKinds.Stops : PlaceKinds.All,
				cancellationToken: cancellationToken)
			.ConfigureAwait(false);

		List<Location> fitting = [.. found.Where(place => !station || place.IsStation)];

		if (fitting.Count == 0)
		{
			DiagnosticLog.Write($"[Fares] '{source.Name}' not found at the VVO WebAPI");

			return null;
		}

		Location best =
			fitting
				.OrderBy(place => string.Equals(place.Name, source.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
				.ThenBy(place => Metres(place, source))
				.First();

		DiagnosticLog.Write($"[Fares] '{source.Name}' -> '{best.Name}' ({Metres(best, source):F0} m)");

		return best;
	}

	private static Location Of(Station station) =>
		new()
		{
			Id = station.Id,
			ProviderId = station.ProviderId,
			Name = station.Name,
			Place = station.Place,
			Latitude = station.Latitude,
			Longitude = station.Longitude
		};

	private static double Metres(Location a, Location b)
	{
		if (a.Latitude is not { } la || a.Longitude is not { } lo || b.Latitude is not { } lb || b.Longitude is not { } ob)
		{
			return double.MaxValue;
		}

		double dLat = (la - lb) * 111_320;
		double dLon = (lo - ob) * 111_320 * Math.Cos(la * Math.PI / 180);

		return Math.Sqrt((dLat * dLat) + (dLon * dLon));
	}

	private static string Prices(Journey journey) =>
		string.Join("|", journey.Fares.Select(fare => $"{fare.Kind}:{fare.Price}"));

	/// <summary>The lines of the rides in order, as written without spaces and case.</summary>
	private static string[] Rides(Journey journey) =>
		[.. journey.Legs
			.Where(leg => leg.IsRide)
			.Select(leg => string.Concat((leg.Line?.Name ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant())];
}
