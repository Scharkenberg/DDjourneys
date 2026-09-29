namespace DDjourneys.Core.Models;

public sealed class JourneyTransfer
{
	public required Station Location { get; init; }

	public TimeSpan Duration { get; init; }

	public bool IsGuaranteed { get; init; }

	public IReadOnlyList<string> Notices { get; init; }
		= Array.Empty<string>();
}