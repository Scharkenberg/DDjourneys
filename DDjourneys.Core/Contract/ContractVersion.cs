using System.Collections.Frozen;

namespace DDjourneys.Core.Contract;

/// <summary>
/// The external contract: how other apps ask DDjourneys to do something (inbound) and how
/// DDjourneys hands results back (outbound). One vocabulary for every transport: a
/// <c>ddjourneys://</c> link, an Android intent with string extras, a Windows protocol launch.
/// The specification lives in docs/EXTERNAL_CONTRACT.md; this file is its reference constants.
/// </summary>
public static class ContractVersion
{
	/// <summary>URI scheme (lower case, registered by the platforms).</summary>
	public const string Scheme = "ddjourneys";

	/// <summary>Android intent action for callers that prefer intents with string extras to links.</summary>
	public const string AndroidAction = "dev.Scharkenberg.DDjourneys.action.CONTRACT";

	/// <summary>The newest version this app speaks. Additive changes keep the number.</summary>
	public const int Current = 1;

	/// <summary>The oldest version this app still answers.</summary>
	public const int Oldest = 1;
}

/// <summary>What a caller can ask for.</summary>
public enum ContractCommand
{
	/// <summary>Fill the planner (and optionally search). Shows the app; replies only on error.</summary>
	Plan,

	/// <summary>Like plan, but the user picks one journey and it is handed back to the caller.</summary>
	Pick,

	/// <summary>Opens the followed journeys, optionally focused on one plan. Replies only on error.</summary>
	Tracked,

	/// <summary>Asks what this app version supports. Needs no UI; replies at once.</summary>
	Capabilities
}

public enum ContractErrorCode
{
	/// <summary>The request could not be read at all.</summary>
	Malformed,

	UnknownCommand,

	/// <summary>The caller asks for a contract version newer than this app speaks.</summary>
	UnsupportedVersion,

	/// <summary>A parameter is present but has an unusable value.</summary>
	InvalidParameter,

	/// <summary>A parameter the command needs is absent.</summary>
	MissingParameter,

	/// <summary>No stop or address was found for a named place.</summary>
	PlaceNotFound,

	/// <summary>A stop key belongs to a provider other than the one selected in the app.</summary>
	ProviderMismatch,

	/// <summary>The pick was not completed in time.</summary>
	Expired,

	/// <summary>Something failed inside the app.</summary>
	Internal
}

public static class ContractWire
{
	/// <summary>Wire name of a command ("plan").</summary>
	public static string Name(this ContractCommand command) =>
		command switch
		{
			ContractCommand.Plan => "plan",
			ContractCommand.Pick => "pick",
			ContractCommand.Tracked => "tracked",
			ContractCommand.Capabilities => "capabilities",
			_ => "unknown"
		};

	/// <summary>Wire name of an error ("place_not_found").</summary>
	public static string Name(this ContractErrorCode code) =>
		code switch
		{
			ContractErrorCode.Malformed => "malformed",
			ContractErrorCode.UnknownCommand => "unknown_command",
			ContractErrorCode.UnsupportedVersion => "unsupported_version",
			ContractErrorCode.InvalidParameter => "invalid_parameter",
			ContractErrorCode.MissingParameter => "missing_parameter",
			ContractErrorCode.PlaceNotFound => "place_not_found",
			ContractErrorCode.ProviderMismatch => "provider_mismatch",
			ContractErrorCode.Expired => "expired",
			_ => "internal"
		};

	private static readonly FrozenDictionary<string, ContractCommand> Commands =
		new Dictionary<string, ContractCommand>(StringComparer.OrdinalIgnoreCase)
		{
			["plan"] = ContractCommand.Plan,
			["pick"] = ContractCommand.Pick,
			["tracked"] = ContractCommand.Tracked,
			["capabilities"] = ContractCommand.Capabilities
		}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	public static bool TryParseCommand(string? text, out ContractCommand command) =>
		Commands.TryGetValue(text?.Trim() ?? string.Empty, out command);
}

/// <summary>Hard limits that keep a hostile or buggy caller from costing anything.</summary>
public static class ContractLimits
{
	public const int MaxKeys = 40;
	public const int MaxValueLength = 200;
	public const int MaxCallbackLength = 2000;
	public const int MaxUriLength = 8000;
	public const int MaxReferenceLength = 64;

	/// <summary>Replies longer than this drop the journey document (flat values stay).</summary>
	public const int MaxReplyLength = 7000;
}
