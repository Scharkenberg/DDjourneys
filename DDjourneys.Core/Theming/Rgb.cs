using System.Globalization;

namespace DDjourneys.Core.Theming;

/// <summary>An sRGB colour with the maths needed for contrast-safe palettes (WCAG 2.x relative luminance).</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
	public static Rgb Black { get; } = new(0, 0, 0);

	public static Rgb White { get; } = new(255, 255, 255);

	/// <summary>Parses "#RRGGBB" or "RRGGBB".</summary>
	public static Rgb Parse(string hex)
	{
		ArgumentNullException.ThrowIfNull(hex);

		ReadOnlySpan<char> span = hex.AsSpan().TrimStart('#');

		if (span.Length != 6)
		{
			throw new FormatException($"Expected RRGGBB, got '{hex}'.");
		}

		return new Rgb(
			byte.Parse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
			byte.Parse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
			byte.Parse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
	}

	public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

	/// <summary>Relative luminance, 0 (black) to 1 (white).</summary>
	public double Luminance =>
		(0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

	/// <summary>WCAG contrast ratio, 1 to 21.</summary>
	public static double Contrast(Rgb a, Rgb b)
	{
		double x = a.Luminance;
		double y = b.Luminance;

		if (x < y)
		{
			(x, y) = (y, x);
		}

		return (x + 0.05) / (y + 0.05);
	}

	/// <summary>Linear blend: 0 returns this, 1 returns <paramref name="other"/>.</summary>
	public Rgb Mix(Rgb other, double t) =>
		new(
			Lerp(R, other.R, t),
			Lerp(G, other.G, t),
			Lerp(B, other.B, t));

	private static byte Lerp(byte a, byte b, double t) =>
		(byte)Math.Clamp((int)Math.Round(a + ((b - a) * t), MidpointRounding.AwayFromZero), 0, 255);

	private static double Linear(byte channel)
	{
		double c = channel / 255.0;

		return c <= 0.03928
			? c / 12.92
			: Math.Pow((c + 0.055) / 1.055, 2.4);
	}
}
