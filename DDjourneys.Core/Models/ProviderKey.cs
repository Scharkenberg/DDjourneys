namespace DDjourneys.Core.Models;

/// <summary>
/// Provider-qualified identity of a stop. The same raw id can denote different stops at different
/// providers, so an id is never compared, stored or used as a dictionary key without its provider.
/// </summary>
public static class ProviderKey
{
	/// <summary>Provider part used for objects that do not carry a provider id (legacy or hand-made).</summary>
	private const string UnknownProvider = "-";

	/// <summary>"vvo:33000028"; null when there is no stop id.</summary>
	public static string? Compose(
		string? providerId,
		string? id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		string provider =
			string.IsNullOrWhiteSpace(providerId)
				? UnknownProvider
				: providerId.Trim().ToLowerInvariant();

		return $"{provider}:{id.Trim()}";
	}

	/// <summary>True when both sides name the same stop of the same provider.</summary>
	public static bool Same(
		string? providerA,
		string? idA,
		string? providerB,
		string? idB)
	{
		string? a = Compose(providerA, idA);
		string? b = Compose(providerB, idB);

		return a is not null
			&& string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}
}
