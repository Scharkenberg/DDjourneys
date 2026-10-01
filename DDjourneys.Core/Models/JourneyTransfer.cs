namespace DDjourneys.Core.Models;

public sealed class JourneyTransfer
{
	public required Station Location { get; init; }


	/// <summary>
	/// Zero-based index of the journey leg immediately before this transfer.
	///
	/// Null means the transfer occurs before the first leg.
	/// </summary>
	public int? PreviousLegIndex { get; init; }


	/// <summary>
	/// Zero-based index of the journey leg immediately after this transfer.
	///
	/// Null means the transfer occurs after the final leg.
	/// </summary>
	public int? NextLegIndex { get; init; }


	/// <summary>
	/// Duration represented by this provider transfer instruction.
	///
	/// This is the duration of this individual instruction, not necessarily
	/// the complete time available between the surrounding journey legs.
	/// </summary>
	public TimeSpan Duration
	{
		get;
		init
		{
			if (value.TotalDays > 1)
			{
				System.Diagnostics.Debug.WriteLine(
					$"!!! INVALID TRANSFER DURATION {value}");
			}

			field = value;
		}
	}


	/// <summary>
	/// Time spent waiting before the next vehicle.
	/// Optional because not all providers expose it.
	/// </summary>
	public TimeSpan? WaitingTime { get; init; }


	public TransferKind Kind { get; init; }
		= TransferKind.Unknown;


	/// <summary>
	/// Geometry of the transfer movement in WGS84 coordinates.
	/// </summary>
	public IReadOnlyList<(double Latitude, double Longitude)> Path { get; init; }
		= [];

	public bool IsGuaranteed { get; init; }


	public string? ArrivalPlatform { get; init; }


	public string? DeparturePlatform { get; init; }


	public IReadOnlyList<string> Notices { get; init; }
		= [];
}