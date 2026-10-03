using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace DDjourneys.Core.Contract;

/// <summary>
/// Decides which URIs DDjourneys may open to answer a caller. A callback is a link the caller
/// controls, so it must never reach anything that acts without the caller's app: files, other
/// content, scripts, calls, messages, system dialogs, or this app itself (a reply loop).
/// Allowed: https, and custom schemes ("dddepartures://cb", "myapp://done").
/// </summary>
public static partial class ContractCallbackPolicy
{
	private static readonly FrozenSet<string> Blocked =
		new[]
		{
			"http", "file", "content", "javascript", "intent", "data", "about", "vbscript", "blob",
			"ftp", "ws", "wss", "tel", "sms", "smsto", "mms", "mmsto", "mailto", "sip", "market",
			"android-app", "ssh", "telnet", "view-source", ContractVersion.Scheme
		}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	[GeneratedRegex("^[a-z][a-z0-9+.-]{1,31}$")]
	private static partial Regex CustomScheme();

	/// <summary>Parses and checks a callback; false (and null) for anything not allowed.</summary>
	public static bool TryParse(string? text, out Uri? uri)
	{
		uri = null;

		if (string.IsNullOrWhiteSpace(text)
			|| text.Length > ContractLimits.MaxCallbackLength
			|| text.Any(char.IsControl)
			|| !Uri.TryCreate(text.Trim(), UriKind.Absolute, out Uri? candidate)
			|| !IsAllowed(candidate))
		{
			return false;
		}

		uri = candidate;

		return true;
	}

	public static bool IsAllowed(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);

		string scheme = uri.Scheme.ToLowerInvariant();

		if (Blocked.Contains(scheme)
			|| scheme.StartsWith("ms-", StringComparison.Ordinal)
			|| scheme.StartsWith("microsoft-", StringComparison.Ordinal)
			|| !string.IsNullOrEmpty(uri.UserInfo))
		{
			return false;
		}

		return scheme == "https"
			? !string.IsNullOrEmpty(uri.Host)
			: CustomScheme().IsMatch(scheme);
	}
}
