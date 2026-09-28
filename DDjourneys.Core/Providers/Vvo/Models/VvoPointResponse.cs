namespace DDjourneys.Core.Providers.Vvo.Models;

/// <summary>
/// Represents a VVO PointFinder API response.
/// </summary>
public sealed class VvoPointResponse
{
	/// <summary>
	/// Response status returned by VVO.
	/// </summary>
	public VvoStatus? Status { get; init; }


	/// <summary>
	/// Points returned by the provider.
	/// </summary>
	public IReadOnlyList<VvoPoint> Points { get; init; }
		= Array.Empty<VvoPoint>();
}


/// <summary>
/// Represents a VVO API status object.
/// </summary>
public sealed class VvoStatus
{
	/// <summary>
	/// Numeric provider status code.
	/// </summary>
	public int Code { get; init; }


	/// <summary>
	/// Provider message.
	/// </summary>
	public string? Message { get; init; }
}