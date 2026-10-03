using DDjourneys.Core.Contract;

namespace DDjourneys.Contract;

/// <summary>A request that is well-formed but cannot be carried out; becomes an error reply.</summary>
internal sealed class ContractRefusal(ContractErrorCode code, string message, string? parameter = null)
	: Exception(message)
{
	public ContractErrorCode Code { get; } = code;

	public string? Parameter { get; } = parameter;
}
