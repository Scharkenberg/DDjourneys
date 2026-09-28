namespace DDjourneys.Core.Models;

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
	/// Indicates whether the request completed successfully.
	///
	/// This does not mean journeys exist.
	/// A successful search can return zero journeys.
	/// </summary>
	public bool IsSuccessful { get; init; }


	/// <summary>
	/// Provider or application error message.
	///
	/// Null when no error occurred.
	/// </summary>
	public string? ErrorMessage { get; init; }


	/// <summary>
	/// Indicates whether any journeys are available.
	/// </summary>
	public bool HasJourneys =>
		Journeys.Count > 0;


	/// <summary>
	/// Creates a successful result.
	/// </summary>
	public static JourneyResult Success(
		IReadOnlyList<Journey> journeys,
		IReadOnlyList<string>? notices = null)
	{
		return new JourneyResult
		{
			IsSuccessful = true,
			Journeys = journeys,
			Notices = notices ?? Array.Empty<string>()
		};
	}


	/// <summary>
	/// Creates a failed result.
	/// </summary>
	public static JourneyResult Failure(
		string message)
	{
		return new JourneyResult
		{
			IsSuccessful = false,
			ErrorMessage = message
		};
	}
}