namespace DDjourneys.Core.Models;

public sealed class JourneyTransfer
{
	public required Station Location { get; init; }


	/// <summary>
	/// Total time reserved for this transfer by the routing engine.
	/// Includes walking and/or waiting depending on transfer kind.
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


	public bool IsGuaranteed { get; init; }


	public string? ArrivalPlatform { get; init; }


	public string? DeparturePlatform { get; init; }


	public IReadOnlyList<string> Notices { get; init; }
		= Array.Empty<string>();
}