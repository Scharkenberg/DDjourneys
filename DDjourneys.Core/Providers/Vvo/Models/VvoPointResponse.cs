using System.Text.Json.Serialization;

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
	/// Raw point entries returned by the provider.
	///
	/// Each entry is a pipe-delimited string.
	/// </summary>
	[JsonPropertyName("Points")]
	public IReadOnlyList<string> RawPoints { get; init; }
		= [];


	/// <summary>
	/// Parsed points returned by the provider.
	///
	/// These objects are created from the raw pipe-delimited entries.
	/// </summary>
	[JsonIgnore]
	public IReadOnlyList<VvoPoint> Points =>
		[.. RawPoints
			.Select(VvoPoint.Parse)];
}


/// <summary>
/// Represents a VVO API status object.
/// </summary>
public sealed class VvoStatus
{
	/// <summary>
	/// Numeric provider status code.
	/// </summary>
	public string? Code { get; init; }


	/// <summary>
	/// Provider message.
	/// </summary>
	public string? Message { get; init; }
}