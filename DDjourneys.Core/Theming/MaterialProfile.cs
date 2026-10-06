namespace DDjourneys.Core.Theming;

/// <summary>
/// Windows 11 window material (Mica, Mica Alt, desktop Acrylic) and how far it reaches into the app.
/// <para>
/// Following the Fluent layering guidance, the material is the base layer of the window and everything that
/// should show it must be transparent; surfaces on top of it are low-opacity layers of the palette, not opaque
/// fills. This table decides how transparent each palette role becomes. Roles: <c>Bg</c> (page background),
/// <c>Surface</c> (cards, panels), <c>Raised</c> (chips, fields, buttons at rest).
/// </para>
/// </summary>
public static class MaterialProfile
{
	public const string None = "none";
	public const string Mica = "mica";
	public const string MicaAlt = "micaalt";
	public const string Acrylic = "acrylic";

	/// <summary>Only the page background is the material; cards stay solid (calmest).</summary>
	public const string Backdrop = "backdrop";

	/// <summary>Fluent card pattern: cards are low-opacity layers over the material.</summary>
	public const string Layered = "layered";

	/// <summary>Everything is a thin layer over the material (most of it shows through).</summary>
	public const string Immersive = "immersive";

	public static IReadOnlyList<string> Materials { get; } = [None, Mica, MicaAlt, Acrylic];

	public static IReadOnlyList<string> Coverages { get; } = [Backdrop, Layered, Immersive];

	public static string NormalizeMaterial(string? id) =>
		Materials.FirstOrDefault(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase)) ?? Mica;

	public static string NormalizeCoverage(string? id) =>
		Coverages.FirstOrDefault(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase)) ?? Layered;

	/// <summary>Opacity (0..255) of a palette role under a material. Opaque when there is no material.</summary>
	public static byte Alpha(string material, string coverage, string role, bool dark)
	{
		if (NormalizeMaterial(material) == None)
		{
			return 255;
		}

		if (role == "Bg")
		{
			return 0;
		}

		bool acrylic = NormalizeMaterial(material) == Acrylic;

		double opacity =
			(NormalizeCoverage(coverage), role) switch
			{
				(Backdrop, _) => 1,
				(Layered, "Surface") => acrylic ? (dark ? 0.56 : 0.64) : (dark ? 0.64 : 0.72),
				(Layered, "Raised") => acrylic ? (dark ? 0.76 : 0.84) : (dark ? 0.82 : 0.90),
				(Immersive, "Surface") => acrylic ? (dark ? 0.40 : 0.48) : (dark ? 0.48 : 0.56),
				(Immersive, "Raised") => acrylic ? (dark ? 0.62 : 0.72) : (dark ? 0.70 : 0.80),
				_ => 1
			};

		return (byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero);
	}
}
