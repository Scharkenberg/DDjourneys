namespace DDjourneys.Core.Models;

/// <summary>
/// How a provider answered. Multi-provider selection depends on telling these apart:
/// only <see cref="Found"/> ends the search, the others fall through to the next provider.
/// </summary>
public enum JourneyOutcome
{
	/// <summary>The provider answered and returned at least one journey.</summary>
	Found,

	/// <summary>The provider answered successfully, but has no journey for this query.</summary>
	Empty,

	/// <summary>
	/// The provider was not asked a question it can answer: endpoints belong to another provider,
	/// have no usable id, or the requested capability is missing. Says nothing about the timetable.
	/// </summary>
	NotSuitable,

	/// <summary>The provider could not answer (network, service error, unreadable response).</summary>
	Failed
}

/// <summary>
/// Represents the outcome of a journey search.
/// </summary>
public sealed class JourneyResult
{
	/// <summary>
	/// Journeys returned by the provider.
	/// </summary>
	public IReadOnlyList<Journey> Journeys { get; init; }
		= Array.Empty<Journey>();


	/// <summary>
	/// Informational or warning messages from the provider.
	///
	/// Examples:
	/// - construction work
	/// - changed service patterns
	/// - partial data
	/// </summary>
	public IReadOnlyList<string> Notices { get; init; }
		= Array.Empty<string>();


	/// <summary>
	/// How the provider answered; see <see cref="JourneyOutcome"/>.
	/// </summary>
	public JourneyOutcome Outcome { get; init; }
		= JourneyOutcome.Failed;


	/// <summary>
	/// Indicates whether the request completed successfully.
	///
	/// This does not mean journeys exist.
	/// A successful search can return zero journeys.
	/// </summary>
	public bool IsSuccessful =>
		Outcome is JourneyOutcome.Found or JourneyOutcome.Empty;


	/// <summary>
	/// Short code or message naming what went wrong (not localized).
	///
	/// Null when no error occurred.
	/// </summary>
	public string? ErrorMessage { get; init; }


	/// <summary>
	/// Technical detail of the failure, such as the HTTP status and the start of the response body.
	/// For logs and diagnostic display; null when there is none.
	/// </summary>
	public string? ErrorDetail { get; init; }


	/// <summary>
	/// Indicates whether any journeys are available.
	/// </summary>
	public bool HasJourneys =>
		Journeys.Count > 0;


	/// <summary>
	/// Creates a successful result. Without journeys it is an <see cref="JourneyOutcome.Empty"/> answer.
	/// </summary>
	public static JourneyResult Success(
		IReadOnlyList<Journey> journeys,
		IReadOnlyList<string>? notices = null)
	{
		ArgumentNullException.ThrowIfNull(journeys);

		return new JourneyResult
		{
			Outcome =
				journeys.Count > 0
					? JourneyOutcome.Found
					: JourneyOutcome.Empty,
			Journeys = journeys,
			Notices = notices ?? Array.Empty<string>()
		};
	}


	/// <summary>
	/// Creates a failed result: the provider could not answer.
	/// </summary>
	public static JourneyResult Failure(
		string message,
		string? detail = null)
	{
		return new JourneyResult
		{
			Outcome = JourneyOutcome.Failed,
			ErrorMessage = message,
			ErrorDetail = detail
		};
	}


	/// <summary>
	/// Creates a result for a provider that cannot answer this kind of request at all.
	/// </summary>
	public static JourneyResult NotSuitable(
		string reason)
	{
		return new JourneyResult
		{
			Outcome = JourneyOutcome.NotSuitable,
			ErrorMessage = reason
		};
	}
}
