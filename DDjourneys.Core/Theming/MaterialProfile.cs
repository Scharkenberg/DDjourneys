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

		// Thin layers: the first release (0.56-0.90) hid the material behind nearly opaque cards. A card is a sheet of
		// tinted glass now, chips and fields on it a little denser so that text on them stays easy to read.
		double opacity =
			(NormalizeCoverage(coverage), role) switch
			{
				(Backdrop, _) => 1,
				(Layered, "Surface") => acrylic ? (dark ? 0.40 : 0.50) : (dark ? 0.50 : 0.60),
				(Layered, "Raised") => acrylic ? (dark ? 0.62 : 0.70) : (dark ? 0.72 : 0.80),
				(Immersive, "Surface") => acrylic ? (dark ? 0.20 : 0.28) : (dark ? 0.30 : 0.38),
				(Immersive, "Raised") => acrylic ? (dark ? 0.42 : 0.52) : (dark ? 0.52 : 0.62),
				_ => 1
			};

		return (byte)Math.Round(opacity * 255, MidpointRounding.AwayFromZero);
	}

	/// <summary>
	/// How the backdrop itself is rendered: the opacity of its tint and of its luminosity layer (both 0..1: lower
	/// lets more of the wallpaper or the windows behind through) and how much of the accent colour is mixed into the tint.
	/// The system's own defaults are tuned to be hardly noticeable; the app asks for more, and more the further the
	/// material reaches into the app (<see cref="Backdrop"/> calm, <see cref="Immersive"/> pronounced).
	/// </summary>
	public static BackdropLook Look(string material, string coverage, bool dark)
	{
		bool acrylic = NormalizeMaterial(material) == Acrylic;

		return (NormalizeCoverage(coverage), acrylic) switch
		{
			(Backdrop, false) => new(dark ? 0.62 : 0.46, 1.00, 0.06),
			(Layered, false) => new(dark ? 0.44 : 0.30, 0.92, 0.12),
			(Immersive, false) => new(dark ? 0.26 : 0.16, 0.80, 0.20),
			(Backdrop, true) => new(dark ? 0.52 : 0.40, 0.78, 0.08),
			(Layered, true) => new(dark ? 0.36 : 0.26, 0.58, 0.16),
			_ => new(dark ? 0.22 : 0.14, 0.38, 0.26)
		};
	}
}

/// <summary>Opacity of the tint (0..1), opacity of the luminosity layer (0..1) and the share of the accent colour in the tint (0..1).</summary>
public readonly record struct BackdropLook(double TintOpacity, double LuminosityOpacity, double AccentMix);
