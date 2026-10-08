namespace DDjourneys.Support;

/// <summary>
/// The selectable font faces and the registered font aliases behind them.
/// MAUI cannot pick weights from a variable font, so Inter Tight ships as two static instances
/// (400 and 600) next to Open Sans Regular and Semibold. "System" uses the platform's own font.
/// </summary>
public static class FontCatalog
{
	public const string SystemId = "system";
	public const string OpenSansId = "opensans";
	public const string InterTightId = "intertight";

	public static IReadOnlyList<string> Ids { get; } = (string[])[SystemId, OpenSansId, InterTightId];

	public static string Normalize(string? id) =>
		Ids.FirstOrDefault(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase))
		?? SystemId;

	/// <summary>FontFamily values for body text and emphasised text.</summary>
	public static (string Regular, string Semibold) Families(string? id) =>
		Normalize(id) switch
		{
			InterTightId => ("InterTightRegular", "InterTightSemiBold"),
			SystemId =>
#if ANDROID
				("sans-serif", "sans-serif-medium"),
#elif WINDOWS
				("Segoe UI", "Segoe UI Semibold"),
#else
				(string.Empty, string.Empty),
#endif
			OpenSansId => ("OpenSansRegular", "OpenSansSemibold"),
			_ => Families(SystemId)
		};
}
