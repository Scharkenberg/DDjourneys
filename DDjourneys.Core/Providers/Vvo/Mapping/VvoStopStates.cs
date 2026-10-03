namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>Reading of the per-stop real-time states of the VVO trip response.</summary>
public static class VvoStopStates
{
	/// <summary>
	/// True for a stop event the vehicle does not perform. The VVO site shows this as "Halt fällt aus" on
	/// the stop and, when it affects a stop of the journey, "Eine Haltestelle wird nicht bedient." above it.
	/// Compared case-insensitively so a change of the provider's casing cannot silently disable it.
	/// </summary>
	public static bool IsCancelled(string? state)
	{
		return string.Equals(state?.Trim(), "Cancelled", StringComparison.OrdinalIgnoreCase);
	}
}
