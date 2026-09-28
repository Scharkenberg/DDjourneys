using DDjourneys.Core.Models;

namespace DDjourneys.Core.Parsing;

/// <summary>
/// Converts provider responses into DDjourneys domain models.
/// </summary>
public sealed class JourneyParser
{
	/// <summary>
	/// Parses a provider response into a journey result.
	/// </summary>
	public JourneyResult Parse(
		string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);

		/*
         * Provider-specific JSON mapping will be implemented here.
         *
         * This class intentionally does not expose the provider model.
         *
         * Example future flow:
         *
         * JSON
         *  |
         * deserialize
         *  |
         * provider DTO
         *  |
         * map
         *  |
         * JourneyResult
         */

		throw new NotImplementedException(
			"Provider response parsing has not been implemented yet.");
	}
}