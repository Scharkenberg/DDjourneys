using Android.Content;
using DDjourneys.Core.Contract;

namespace DDjourneys.Platforms.Android;

/// <summary>
/// Reads a contract request from an incoming intent: a VIEW of a <c>ddjourneys://</c> link, or the
/// CONTRACT action with string extras named like the link's query keys (plus <c>command</c> and, optionally, <c>v</c>).
/// Returns null for any other intent. Everything else is the parser's job.
/// </summary>
internal static class ContractIntentReader
{
	public static ContractParseResult? Read(Intent intent)
	{
		try
		{
			if (intent.Action == Intent.ActionView
				&& intent.Data is { } data
				&& string.Equals(data.Scheme, ContractVersion.Scheme, StringComparison.OrdinalIgnoreCase))
			{
				return ContractParser.ParseUri(data.ToString());
			}

			if (intent.Action != ContractVersion.AndroidAction)
			{
				return null;
			}

			var bag = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

			if (intent.Extras is { } extras)
			{
				foreach (string key in extras.KeySet() ?? [])
				{
					// Only strings are part of the contract; anything else reads as null and is skipped.
					if (bag.Count > ContractLimits.MaxKeys)
					{
						break;
					}

					bag[key] = extras.GetString(key);
				}
			}

			return ContractParser.Parse(bag);
		}
		catch (Exception ex)
		{
			// A hostile bundle can throw while it is unparcelled.
			System.Diagnostics.Debug.WriteLine($"Contract intent unreadable: {ex.Message}");

			return ContractParseResult.Fail(
				new ContractFailure(ContractErrorCode.Malformed, "The intent could not be read."));
		}
	}
}
