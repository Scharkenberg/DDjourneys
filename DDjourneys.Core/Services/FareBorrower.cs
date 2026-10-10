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

			Location? from = await ResolveAsync((journey.Origin ?? journey.From).Name, cancellationToken).ConfigureAwait(false);
			Location? to = await ResolveAsync((journey.Destination ?? journey.To).Name, cancellationToken).ConfigureAwait(false);

			if (from is null || to is null)
			{
				DiagnosticLog.Write("[Fares] no ticket prices borrowed: a stop is unknown to the VVO WebAPI");

				return [];
			}

			JourneyResult result =
				await journeys.SearchAsync(
					new JourneyQuery
					{
						From = from,
						To = to,
						DateTime = start - TimeSpan.FromMinutes(3),
						MaxResults = 6
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

	private async Task<Location?> ResolveAsync(string name, CancellationToken cancellationToken)
	{
		IReadOnlyList<Location> found =
			await locations.SearchAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);

		return found.FirstOrDefault(stop => stop.IsStation && string.Equals(stop.Name, name, StringComparison.OrdinalIgnoreCase))
			?? found.FirstOrDefault(stop => stop.IsStation);
	}

	/// <summary>The lines of the rides in order, as written without spaces and case.</summary>
	private static string[] Rides(Journey journey) =>
		[.. journey.Legs
			.Where(leg => leg.IsRide)
			.Select(leg => string.Concat((leg.Line?.Name ?? string.Empty).Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant())];
}
