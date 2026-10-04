namespace DDjourneys.Core.Models;

/// <summary>Where a position came from.</summary>
public enum VehicleSource
{
	Unknown,

	/// <summary>A radio telegram of the vehicle (R09).</summary>
	Telegram,

	Gps
}


/// <summary>One live position of a vehicle.</summary>
public sealed class LiveVehicle
{
	/// <summary>Line number as the transport company numbers it internally.</summary>
	public int Line { get; init; }

	/// <summary>Identifies one vehicle run of the line.</summary>
	public int Run { get; init; }

	public double Latitude { get; init; }

	public double Longitude { get; init; }

	public DateTimeOffset Time { get; init; }

	/// <summary>Positive: late. Null when the source does not say.</summary>
	public TimeSpan? Delay { get; init; }

	public VehicleSource Source { get; init; }

	public int Region { get; init; }

	public string Key =>
		FormattableString.Invariant($"{Region}:{Line}:{Run}");
}


/// <summary>What the passenger wants to see.</summary>
public sealed class VehicleFilter
{
	/// <summary>Empty: all lines.</summary>
	public IReadOnlyList<int> Lines { get; init; } = [];

	/// <summary>0 is Dresden.</summary>
	public int Region { get; init; }
}
