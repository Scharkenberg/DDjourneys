using DDjourneys.Core.Diagnostics;

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
				DiagnosticLog.Write(
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

	/// <summary>
	/// The provider does not flag this change as endangered (also true for an ensured connection).
	/// </summary>
	public bool IsGuaranteed { get; init; }

	/// <summary>
	/// The provider says the connecting vehicle waits for this one (an ensured connection, "Anschlusssicherung",
	/// as at Dresden's Postplatz at minute 45). Real-time arithmetic must never call such a change impossible.
	/// </summary>
	public bool IsEnsured { get; init; }

	/// <summary>The provider's own object this transfer was mapped from (diagnostics only).</summary>
	public object? ProviderData { get; init; }


	public string? ArrivalPlatform { get; init; }


	public PlatformKind ArrivalPlatformKind { get; init; }


	public string? DeparturePlatform { get; init; }


	public PlatformKind DeparturePlatformKind { get; init; }

	public Station? From { get; init; }

	public Station? To { get; init; }


	public IReadOnlyList<string> Notices { get; init; }
		= [];
}