namespace DDjourneys.Core.Theming;

/// <summary>One of the four line-colour sets: the standard colours, or a remap that stays distinct for one kind of
/// colour-vision deficiency. The keys are the transport modes (the "line colours" of chips, map lines and legends).</summary>
public enum ColourVisionSet
{
	Standard = 0,

	/// <summary>Safe for deuteranopia (red-green, the most common).</summary>
	Deuteranopia,

	/// <summary>Safe for protanopia (red-green).</summary>
	Protanopia,

	/// <summary>Safe for tritanopia (blue-yellow, rare).</summary>
	Tritanopia
}

/// <summary>
/// The line colours of the transport modes, as one switchable set. The standard values are the app's own
/// (Resources/Styles/Tokens.xaml in the MAUI head); the three colour-vision remaps were chosen so that, under the
/// simulated deficiency (<see cref="ColourVisionMath.Simulate"/>), every pair of modes keeps a colour difference of
/// at least 12 CIE-Lab units and every colour keeps a contrast of at least 4.5:1 against white text - the remaps
/// also stay reasonably distinct under the other two deficiencies (at least 10 units), so one wrong pick is never
/// worse than the standard set.
/// </summary>
public static class ModeColorSets
{
	public const string StandardId = "standard";
	public const string DeuteranopiaId = "deuteranopia";
	public const string ProtanopiaId = "protanopia";
	public const string TritanopiaId = "tritanopia";

	public static IReadOnlyList<string> Ids { get; } =
		[StandardId, DeuteranopiaId, ProtanopiaId, TritanopiaId];

	public static string IdOf(ColourVisionSet set) =>
		set switch
		{

			ColourVisionSet.Deuteranopia => DeuteranopiaId,
			ColourVisionSet.Protanopia => ProtanopiaId,
			ColourVisionSet.Tritanopia => TritanopiaId,
			_ => StandardId
		};

	public static ColourVisionSet Of(string? id) =>
		id switch
		{
			DeuteranopiaId => ColourVisionSet.Deuteranopia,
			ProtanopiaId => ColourVisionSet.Protanopia,
			TritanopiaId => ColourVisionSet.Tritanopia,
			_ => ColourVisionSet.Standard
		};

	public static string Normalize(string? id)
	{
		ColourVisionSet set = Of(id);

		return IdOf(set);
	}

	/// <summary>The eight line colours of a set, in the fixed order of <see cref="Modes"/> (the Tokens.xaml order).</summary>
	public static IReadOnlyList<Rgb> ColorsOf(ColourVisionSet set) =>
		set switch
		{
			ColourVisionSet.Deuteranopia => Deuteranopia,
			ColourVisionSet.Protanopia => Protanopia,
			ColourVisionSet.Tritanopia => Tritanopia,
			_ => Standard
		};

	/// <summary>The mode names in the fixed order of the colour tables (the Tokens.xaml resource keys).</summary>
	public static IReadOnlyList<string> Modes { get; } =
		["Tram", "Bus", "Suburban", "Train", "Ferry", "CableCar", "Walk", "Bike"];

	/// <summary>The standard colours, exactly the values of Tokens.xaml (ModeTram ... ModeBike).</summary>
	public static readonly IReadOnlyList<Rgb> Standard =
	[
		new(0xB3, 0x26, 0x1E), // Tram
		new(0x6B, 0x3F, 0xA0), // Bus
		new(0x1F, 0x7A, 0x4D), // Suburban
		new(0x4B, 0x5B, 0x66), // Train
		new(0x1D, 0x5F, 0xA8), // Ferry
		new(0x8A, 0x5A, 0x00), // CableCar
		new(0x5B, 0x6B, 0x74), // Walk
		new(0x0F, 0x76, 0x6E) // Bike
	];

	/// <summary>Deuteranopia-safe: separated along lightness and the blue-yellow axis (the red-green axis collapses).</summary>
	public static readonly IReadOnlyList<Rgb> Deuteranopia =
	[
		new(0x00, 0x1F, 0x4D), // Tram: very dark navy
		new(0x6E, 0x10, 0x70), // Bus: violet
		new(0x5C, 0x0A, 0x0A), // Suburban: very dark red
		new(0x62, 0x76, 0x83), // Train: grey-blue
		new(0x2E, 0x6F, 0xB7), // Ferry: mid blue
		new(0x8F, 0x6E, 0x00), // CableCar: dark yellow
		new(0x3A, 0x4A, 0x55), // Walk: dark slate
		new(0x0F, 0x4C, 0x2E) // Bike: dark green
	];

	/// <summary>Protanopia-safe: lightness and blue-yellow again, with the greens dark enough to stay apart.</summary>
	public static readonly IReadOnlyList<Rgb> Protanopia =
	[
		new(0x6E, 0x10, 0x70), // Tram: violet
		new(0x00, 0x1F, 0x4D), // Bus: very dark navy
		new(0x14, 0x60, 0x3A), // Suburban: dark green
		new(0x62, 0x76, 0x83), // Train: grey-blue
		new(0x2E, 0x6F, 0xB7), // Ferry: mid blue
		new(0x8F, 0x6E, 0x00), // CableCar: dark yellow
		new(0x3A, 0x4A, 0x55), // Walk: dark slate
		new(0x5C, 0x0A, 0x0A) // Bike: very dark red
	];

	/// <summary>Tritanopia-safe: lightness and the red-green axis (the blue-yellow axis collapses).</summary>
	public static readonly IReadOnlyList<Rgb> Tritanopia =
	[
		new(0x8F, 0x2A, 0x2A), // Tram: red
		new(0x4A, 0x0E, 0x8C), // Bus: dark violet
		new(0x1C, 0x7A, 0x4A), // Suburban: green
		new(0x62, 0x76, 0x83), // Train: grey-blue
		new(0x00, 0x1F, 0x4D), // Ferry: very dark navy
		new(0x8F, 0x6E, 0x00), // CableCar: dark yellow
		new(0x3A, 0x4A, 0x55), // Walk: dark slate
		new(0xB0, 0x4F, 0xA0) // Bike: magenta
	];
}

/// <summary>
/// Colour-vision simulation and the checks the remaps are held to. The simulation follows Viénot, Brettel and
/// Mollon (1999): the dichromat's collapsed LMS plane is projected so the surviving axes (lightness, and the axis
/// the deficiency spares) stay. It is the standard quick model used for palette checks; the exact numbers are not
/// clinical, the margins are chosen wide enough that the verdict is robust.
/// </summary>
public static class ColourVisionMath
{
	/// <summary>Simulates one deficiency for an sRGB colour; the result is the colour as that viewer most likely sees it.</summary>
	public static (double R, double G, double B) Simulate(Rgb color, ColourVisionSet deficiency)
	{
		// sRGB -> linear -> XYZ -> LMS (Smith-Pokorny fundamentals)
		double r = Linear(color.R / 255.0), g = Linear(color.G / 255.0), b = Linear(color.B / 255.0);

		double xr = 0.4124 * r + 0.3576 * g + 0.1805 * b;
		double yr = 0.2126 * r + 0.7152 * g + 0.0722 * b;
		double zr = 0.0193 * r + 0.1192 * g + 0.9505 * b;

		double l = 0.15514 * xr + 0.54312 * yr - 0.03286 * zr;
		double m = -0.15544 * xr + 0.28348 * yr + 0.02279 * zr;
		double s = 0.00808 * xr - 0.01377 * yr + 0.86052 * zr;

		switch (deficiency)
		{
			case ColourVisionSet.Deuteranopia:
				l = m;
				break;

			case ColourVisionSet.Protanopia:
				m = l;
				break;

			case ColourVisionSet.Tritanopia:
				s = 0;
				break;
			default:
				return (color.R / 255.0, color.G / 255.0, color.B / 255.0);
		}

		// back: LMS -> XYZ (the inverse of the matrix above, five places) -> linear RGB -> sRGB
		double x = 2.20753 * l - 4.21989 * m + 0.19606 * s;
		double y = 1.21056 * l + 1.20896 * m + 0.01421 * s;
		double z = -0.00136 * l + 0.05897 * m + 1.16047 * s;

		r = 3.2406 * x - 1.5372 * y - 0.4986 * z;
		g = -0.9689 * x + 1.8758 * y + 0.0415 * z;
		b = 0.0557 * x - 0.2040 * y + 1.0570 * z;

		return (Gamma(Clip(r)), Gamma(Clip(g)), Gamma(Clip(b)));
	}

	/// <summary>CIE-Lab of the simulated colour (the difference space of the distinctness check).</summary>
	public static (double L, double A, double B) LabOf(Rgb color, ColourVisionSet deficiency)
	{
		(double sr, double sg, double sb) = Simulate(color, deficiency);

		// The simulated values are gamma-encoded sRGB; the XYZ matrix needs them linear.
		double r = Linear(sr), g = Linear(sg), b = Linear(sb);

		double x = 0.4124 * r + 0.3576 * g + 0.1805 * b;
		double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
		double z = 0.0193 * r + 0.1192 * g + 0.9505 * b;

		(double fx, double fy, double fz) = (F(x / 0.95047), F(y), F(z / 1.08883));

		return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));

		static double F(double t) =>
			t > 0.008856
				? Math.Cbrt(t)
				: 7.787 * t + 16.0 / 116.0;
	}

	/// <summary>The colour difference of two colours under a deficiency (CIE76 in Lab: the plain distance).</summary>
	public static double Difference(Rgb first, Rgb second, ColourVisionSet deficiency)
	{
		(double l1, double a1, double b1) = LabOf(first, deficiency);
		(double l2, double a2, double b2) = LabOf(second, deficiency);

		return Math.Sqrt(
			(l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
	}

	/// <summary>WCAG contrast of a colour against white (the chips draw white text on the mode colours).</summary>
	public static double ContrastWithWhite(Rgb color)
	{
		double y = 0.2126 * Linear(color.R / 255.0) + 0.7152 * Linear(color.G / 255.0) + 0.0722 * Linear(color.B / 255.0);

		return 1.05 / (y + 0.05);
	}

	private static double Linear(double c) =>
		c <= 0.04045
			? c / 12.92
			: Math.Pow((c + 0.055) / 1.055, 2.4);

	private static double Gamma(double c) =>
		c <= 0.0031308
			? 12.92 * c
			: 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;

	private static double Clip(double c) =>
		Math.Clamp(c, 0.0, 1.0);
}
