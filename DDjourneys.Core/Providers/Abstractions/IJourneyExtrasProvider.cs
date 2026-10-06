using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Abstractions;

/// <summary>Things a provider can do with a journey it returned.</summary>
public interface IJourneyExtrasProvider
{
	/// <summary>
	/// Alternatives for one leg of a journey (the connection with that leg taken earlier or later).
	/// </summary>
	Task<JourneyResult> GetLegAlternativeAsync(
		JourneyQuery query,
		Journey journey,
		int legIndex,
		bool previous,
		CancellationToken cancellationToken = default);

	/// <summary>Address of a printable (PDF) version of the journey, or null when there is none.</summary>
	Uri? GetJourneyDocumentUri(
		JourneyQuery query,
		Journey journey);

	/// <summary>Downloads the printable version; null when the provider could not deliver it.</summary>
	Task<JourneyDocument?> GetJourneyDocumentAsync(
		JourneyQuery query,
		Journey journey,
		CancellationToken cancellationToken = default);
}
