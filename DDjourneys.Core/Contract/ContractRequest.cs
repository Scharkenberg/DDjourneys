using DDjourneys.Core.Models;

namespace DDjourneys.Core.Contract;

/// <summary>A place as the caller names it: any combination of name, stop key and coordinates.</summary>
/// <param name="Name">Display or search name.</param>
/// <param name="StopKey">Provider-qualified stop ("vvo:33000028"), see <see cref="ProviderKey"/>.</param>
public sealed record ContractPlace(
	string? Name,
	string? StopKey,
	double? Latitude,
	double? Longitude)
{
	public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;

	public bool IsEmpty => Name is null && StopKey is null && !HasCoordinates;
}

/// <summary>The requested moment: "now", an absolute instant, or a wall-clock time in the provider zone.</summary>
public sealed record ContractTime(
	bool IsNow,
	DateTimeOffset? Absolute,
	DateTime? Wall);

/// <summary>Where results go. Both are validated by <see cref="ContractCallbackPolicy"/> already.</summary>
public sealed record ContractCallbacks(
	Uri? Success,
	Uri? Error)
{
	public static readonly ContractCallbacks None = new(null, null);

	public bool IsEmpty => Success is null && Error is null;
}

/// <summary>A valid, normalized request. Everything in it has been checked; consumers need not re-validate.</summary>
public sealed record ContractRequest
{
	public required ContractCommand Command { get; init; }

	public int Version { get; init; } = ContractVersion.Current;

	public ContractPlace? From { get; init; }

	public ContractPlace? To { get; init; }

	/// <summary>Plan, pick and go: a stop over between start and destination.</summary>
	public ContractPlace? Via { get; init; }

	/// <summary>Departures and map: the place concerned (departures: the stop; map: the centre). Null: where the device is.</summary>
	public ContractPlace? At { get; init; }

	/// <summary>Disruptions and live: the line; departures: only this line.</summary>
	public string? Line { get; init; }

	public ContractTime? Time { get; init; }

	/// <summary>Null: keep what the app uses (departure unless the user prefers arrival).</summary>
	public JourneySearchMode? Mode { get; init; }

	/// <summary>Search right away instead of only filling the planner. Always true for pick.</summary>
	public bool Search { get; init; }

	/// <summary>Tracked: plan to bring into view; null shows the overview.</summary>
	public string? PlanId { get; init; }

	public ContractCallbacks Callbacks { get; init; } = ContractCallbacks.None;

	/// <summary>Opaque caller token, echoed in every reply.</summary>
	public string? Reference { get; init; }

	/// <summary>Things that were ignored (unknown or duplicate keys). Informational, never fatal.</summary>
	public IReadOnlyList<string> Warnings { get; init; } = [];

	/// <summary>Identity of the content (without the reference), used to drop double deliveries.</summary>
	public string Fingerprint { get; init; } = string.Empty;
}

/// <summary>Why a request was refused, with whatever could still be read to reply to the caller.</summary>
public sealed record ContractFailure(
	ContractErrorCode Code,
	string Message,
	string? Parameter = null,
	ContractCommand? Command = null,
	string? Reference = null,
	ContractCallbacks? Callbacks = null);

/// <summary>Outcome of parsing: exactly one of <see cref="Request"/> and <see cref="Failure"/> is set.</summary>
public sealed class ContractParseResult
{
	private ContractParseResult(ContractRequest? request, ContractFailure? failure)
	{
		Request = request;
		Failure = failure;
	}

	public ContractRequest? Request { get; }

	public ContractFailure? Failure { get; }

	public bool IsValid => Request is not null;

	public static ContractParseResult Ok(ContractRequest request) => new(request, null);

	public static ContractParseResult Fail(ContractFailure failure) => new(null, failure);
}
