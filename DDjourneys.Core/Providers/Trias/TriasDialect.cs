namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// A TRIAS schema version a request is written for. Requests start at 1.4 and step down one version at a
/// time (1.3, 1.2, 1.1) for as long as the server rejects them.
/// </summary>
/// <remarks>
/// Only 1.4 is in the VDVde/TRIAS repository. Which optional parameters older schemas know is therefore
/// an assumption: 1.3 and later get the extended content flags, 1.4 the accessibility additions, anything
/// older only the parameters every version has. A server that rejects a parameter is handled by the next step down.
/// </remarks>
internal readonly record struct TriasDialect(int Minor)
{
	public static TriasDialect Latest { get; } = new(4);

	public static TriasDialect Oldest { get; } = new(1);

	public string Version =>
		"1." + Minor;

	/// <summary>Extended content flags: situations, estimated times, accessibility, operating days.</summary>
	public bool HasExtendedContent =>
		Minor >= 3;

	/// <summary>Ramp and level-entrance filters, immediate start.</summary>
	public bool HasMobilityAdditions =>
		Minor >= 4;

	/// <summary>The next older version, or null after the oldest.</summary>
	public TriasDialect? Older =>
		Minor > Oldest.Minor
			? new TriasDialect(Minor - 1)
			: null;
}

/// <summary>The kinds of request whose working version is remembered separately.</summary>
internal enum TriasRequestKind
{
	Locations,
	Trip,
	StopEvent,
	TripInfo
}
