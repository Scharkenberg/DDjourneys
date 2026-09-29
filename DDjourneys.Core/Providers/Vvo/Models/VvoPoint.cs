namespace DDjourneys.Core.Providers.Vvo.Models;

/// <summary>
/// Represents one raw point returned by the VVO PointFinder API.
///
/// This is a provider DTO and must not leak into the application layer.
/// </summary>
public sealed class VvoPoint
{
	/// <summary>
	/// Provider identifier.
	///
	/// Usually a numeric stop ID.
	/// </summary>
	public string Id { get; init; } = string.Empty;


	/// <summary>
	/// Optional point type information.
	///
	/// Examples:
	/// stop
	/// POI
	/// coordinate
	/// </summary>
	public string Type { get; init; } = string.Empty;


	/// <summary>
	/// City or locality.
	/// </summary>
	public string Place { get; init; } = string.Empty;


	/// <summary>
	/// Human-readable name.
	/// </summary>
	public string? Name { get; init; }


	/// <summary>
	/// Raw first coordinate value returned by VVO.
	///
	/// Usually GK4 latitude.
	/// </summary>
	public string? Coordinate1 { get; init; }


	/// <summary>
	/// Raw second coordinate value returned by VVO.
	///
	/// Usually GK4 longitude.
	/// </summary>
	public string? Coordinate2 { get; init; }


	/// <summary>
	/// Remaining provider-specific fields.
	/// </summary>
	public IReadOnlyList<string> AdditionalData { get; init; }
		= Array.Empty<string>();


	/// <summary>
	/// Original unmodified response string.
	/// Useful for diagnostics.
	/// </summary>
	public required string Raw { get; init; }

	public bool IsStop => int.TryParse(Id, out _);

	/// <summary>
	/// Parses a raw pipe-delimited PointFinder response entry into a VVO point.
	/// </summary>
	/// <param name="raw">
	/// The original VVO PointFinder string.
	/// </param>
	/// <returns>
	/// A parsed <see cref="VvoPoint"/> instance.
	/// </returns>
	public static VvoPoint Parse(string raw)
	{
		string[] fields = raw.Split('|');

		return new VvoPoint
		{
			Raw = raw,

			Id = fields.ElementAtOrDefault(0) ?? string.Empty,

			Type = fields.ElementAtOrDefault(1) ?? string.Empty,

			Place = fields.ElementAtOrDefault(2) ?? string.Empty,

			Name = fields.ElementAtOrDefault(3),

			Coordinate1 = fields.ElementAtOrDefault(4),

			Coordinate2 = fields.ElementAtOrDefault(5),

			AdditionalData =
				fields.Length > 6
					? fields[6..]
					: Array.Empty<string>()
		};
	}

}