using System.Text;

namespace DDjourneys.Core.Contract;

/// <summary>
/// What DDjourneys sends back to a caller: a flat list of key/value pairs appended to the
/// caller's <c>x-success</c> / <c>x-error</c> URI. Every reply carries <c>contract</c>,
/// <c>status</c> and, when known, <c>command</c> and the caller's <c>ref</c>; errors add
/// <c>code</c> and an English <c>message</c> meant for logs, not for display.
/// </summary>
public sealed class ContractReply
{
	private ContractReply(
		ContractCommand? command,
		string? reference,
		bool isSuccess,
		ContractErrorCode? code,
		string? message,
		IReadOnlyList<KeyValuePair<string, string>> values)
	{
		Command = command;
		Reference = reference;
		IsSuccess = isSuccess;
		Code = code;
		Message = message;
		Values = values;
	}

	public ContractCommand? Command { get; }

	public string? Reference { get; }

	public bool IsSuccess { get; }

	public ContractErrorCode? Code { get; }

	public string? Message { get; }

	/// <summary>Command-specific pairs after the common ones.</summary>
	public IReadOnlyList<KeyValuePair<string, string>> Values { get; }

	public static ContractReply Success(
		ContractRequest request,
		IEnumerable<KeyValuePair<string, string>>? values = null)
	{
		ArgumentNullException.ThrowIfNull(request);

		return new ContractReply(
			request.Command,
			request.Reference,
			true,
			null,
			null,
			values?.ToList() ?? []);
	}

	public static ContractReply Failure(ContractFailure failure)
	{
		ArgumentNullException.ThrowIfNull(failure);

		return new ContractReply(
			failure.Command,
			failure.Reference,
			false,
			failure.Code,
			failure.Message,
			failure.Parameter is null
				? Array.Empty<KeyValuePair<string, string>>()
				: new[] { new KeyValuePair<string, string>("parameter", failure.Parameter) });
	}

	public static ContractReply Failure(
		ContractRequest request,
		ContractErrorCode code,
		string message)
	{
		ArgumentNullException.ThrowIfNull(request);

		return new ContractReply(request.Command, request.Reference, false, code, message, []);
	}

	/// <summary>All pairs in wire order.</summary>
	public IEnumerable<KeyValuePair<string, string>> Pairs()
	{
		yield return new("contract", ContractVersion.Current.ToString(System.Globalization.CultureInfo.InvariantCulture));
		yield return new("status", IsSuccess ? "ok" : "error");

		if (Command is { } command)
		{
			yield return new("command", command.Name());
		}

		if (Reference is not null)
		{
			yield return new("ref", Reference);
		}

		if (Code is { } code)
		{
			yield return new("code", code.Name());
		}

		if (Message is not null)
		{
			yield return new("message", Message);
		}

		foreach (KeyValuePair<string, string> pair in Values)
		{
			yield return pair;
		}
	}

	/// <summary>
	/// The caller's URI with this reply appended; null when the caller gave no address for this
	/// kind of reply (successes go to <c>x-success</c>, errors only to <c>x-error</c>).
	/// Oversized replies drop the keys named in <paramref name="droppable"/> and add
	/// <c>truncated=1</c> instead of failing.
	/// </summary>
	public Uri? ToCallbackUri(ContractCallbacks callbacks, params string[] droppable)
	{
		ArgumentNullException.ThrowIfNull(callbacks);

		Uri? target = IsSuccess ? callbacks.Success : callbacks.Error;

		if (target is null)
		{
			return null;
		}

		string built = Append(target.OriginalString, Pairs());

		if (built.Length <= ContractLimits.MaxReplyLength || droppable.Length == 0)
		{
			return new Uri(built, UriKind.Absolute);
		}

		var kept =
			Pairs()
				.Where(pair => !droppable.Contains(pair.Key, StringComparer.Ordinal))
				.Append(new("truncated", "1"));

		return new Uri(Append(target.OriginalString, kept), UriKind.Absolute);
	}

	/// <summary>Appends pairs to a URI string, keeping an existing query and fragment intact.</summary>
	public static string Append(string uri, IEnumerable<KeyValuePair<string, string>> pairs)
	{
		string fragment = string.Empty;
		int hash = uri.IndexOf('#');

		if (hash >= 0)
		{
			fragment = uri[hash..];
			uri = uri[..hash];
		}

		var builder = new StringBuilder(uri);
		char separator =
			uri.Contains('?')
				? (uri[^1] is '?' or '&' ? '\0' : '&')
				: '?';

		foreach ((string key, string value) in pairs)
		{
			if (separator != '\0')
			{
				builder.Append(separator);
			}

			builder.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
			separator = '&';
		}

		return builder.Append(fragment).ToString();
	}
}

/// <summary>What <c>capabilities</c> answers with.</summary>
public static class ContractCapabilities
{
	public static IReadOnlyList<KeyValuePair<string, string>> Values(string appVersion) =>
		[
			new("app", "DDjourneys"),
			new("app.version", appVersion),
			new("scheme", ContractVersion.Scheme),
			new("oldest", ContractVersion.Oldest.ToString(System.Globalization.CultureInfo.InvariantCulture)),
			new("commands", string.Join(',', Enum.GetValues<ContractCommand>().Select(command => command.Name()))),
			new("android.action", ContractVersion.AndroidAction),
			new("keywords", string.Join(',', ContractKeywords.Here, ContractKeywords.Home, ContractKeywords.Start)),
			new("android.intents", "view:ddjourneys,view:geo,send:text/plain")
		];
}
