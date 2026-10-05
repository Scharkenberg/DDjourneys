namespace DDjourneys.Core.Theming;

/// <summary>The twelve colours of one theme (same keys the app's resource dictionary uses).</summary>
public sealed record PaletteColors(
	Rgb Bg,
	Rgb Surface,
	Rgb Raised,
	Rgb Outline,
	Rgb Ink,
	Rgb InkMuted,
	Rgb Accent,
	Rgb OnAccent,
	Rgb AccentSoft,
	Rgb OnTime,
	Rgb Delay,
	Rgb Cancelled);

/// <summary>
/// Derives a complete palette from one accent colour (e.g. the system accent).
/// Neutrals are fixed per mode (plain or "solarized" warm paper / teal-black); the accent is moved
/// towards white (dark) or black (light) until it reaches 4.5:1 on Bg and Surface.
/// The soft accent tint keeps Ink at 4.5:1 or better.
/// </summary>
public static class PaletteBuilder
{
	private static readonly Neutrals LightPlain = new(
		"#F2F4F5", "#FFFFFF", "#DFE6E9", "#B4C1C7", "#0F1A1F", "#4A5960", "#15803D", "#9A5B00", "#B91C1C");

	private static readonly Neutrals LightSolarized = new(
		"#FDF6E3", "#FFFBF0", "#EEE8D5", "#8A836D", "#1F3A44", "#52686F", "#1B6E2E", "#7A5A00", "#B3261E");

	private static readonly Neutrals DarkPlain = new(
		"#0C1418", "#16232A", "#23343E", "#456070", "#E9EFF1", "#A2B3BC", "#4ADE80", "#FFC94D", "#F87171");

	private static readonly Neutrals DarkSolarized = new(
		"#002B36", "#073642", "#0F4552", "#6B8791", "#EEE8D5", "#A9B8B8", "#4ADE80", "#FFC94D", "#F87171");

	/// <param name="seed">Wanted accent; it is adjusted for legibility, never rejected.</param>
	/// <param name="dark">Dark neutrals instead of light ones.</param>
	/// <param name="solarized">Warm solarized neutrals instead of plain ones.</param>
	/// <param name="pureBlack">Dark only: Bg and Surface become #000000 (the former Surface becomes Raised).</param>
	/// <param name="tint">Mixes a little of the seed into the neutrals so colour sets differ in their surfaces too, not only in the accent (skipped where it would cost legibility).</param>
	public static PaletteColors Build(Rgb seed, bool dark, bool solarized, bool pureBlack = false, bool tint = false)
	{
		Neutrals n =
			dark
				? solarized ? DarkSolarized : DarkPlain
				: solarized ? LightSolarized : LightPlain;

		Rgb bg = n.Bg;
		Rgb surface = n.Surface;
		Rgb raised = n.Raised;

		if (dark && pureBlack)
		{
			raised = surface;
			bg = Rgb.Black;
			surface = Rgb.Black;
		}

		Rgb outline = n.Outline;

		if (tint)
		{
			double amount = dark ? 0.07 : 0.05;

			Rgb tintedBg = pureBlack && dark ? bg : bg.Mix(seed, amount);
			Rgb tintedSurface = pureBlack && dark ? surface : surface.Mix(seed, amount * 0.5);
			Rgb tintedRaised = raised.Mix(seed, amount * 1.6);

			bool legible =
				new[] { tintedBg, tintedSurface, tintedRaised }.All(
					bgColor => Rgb.Contrast(n.Ink, bgColor) >= 7.0
						&& Rgb.Contrast(n.InkMuted, bgColor) >= 4.5
						&& Rgb.Contrast(n.OnTime, bgColor) >= 4.5
						&& Rgb.Contrast(n.Delay, bgColor) >= 4.5
						&& Rgb.Contrast(n.Cancelled, bgColor) >= 4.5);

			if (legible)
			{
				bg = tintedBg;
				surface = tintedSurface;
				raised = tintedRaised;
				outline = outline.Mix(seed, amount * 1.4);
			}
		}

		Rgb accent = EnsureLegible(seed, bg, surface, dark);
		Rgb onAccent = Rgb.Contrast(accent, Rgb.White) >= Rgb.Contrast(accent, Rgb.Black) ? Rgb.White : Rgb.Black;

		return new PaletteColors(
			bg, surface, raised, outline, n.Ink, n.InkMuted,
			accent, onAccent, SoftTint(raised, accent, n.Ink, dark),
			n.OnTime, n.Delay, n.Cancelled);
	}

	private static Rgb EnsureLegible(Rgb seed, Rgb bg, Rgb surface, bool dark)
	{
		Rgb target = dark ? Rgb.White : Rgb.Black;
		Rgb accent = seed;

		for (int step = 1; step <= 50 && Worst(accent, bg, surface) < 4.5; step++)
		{
			accent = seed.Mix(target, step * 0.02);
		}

		return accent;
	}

	private static double Worst(Rgb accent, Rgb bg, Rgb surface) =>
		Math.Min(Rgb.Contrast(accent, bg), Rgb.Contrast(accent, surface));

	private static Rgb SoftTint(Rgb raised, Rgb accent, Rgb ink, bool dark)
	{
		double t = dark ? 0.24 : 0.18;
		Rgb soft = raised.Mix(accent, t);

		while (t > 0.02
			&& (Rgb.Contrast(ink, soft) < 4.5 || Rgb.Contrast(accent, soft) < 3.0))
		{
			t -= 0.02;
			soft = raised.Mix(accent, t);
		}

		return soft;
	}

	private sealed record Neutrals(
		Rgb Bg, Rgb Surface, Rgb Raised, Rgb Outline, Rgb Ink, Rgb InkMuted, Rgb OnTime, Rgb Delay, Rgb Cancelled)
	{
		public Neutrals(
			string bg, string surface, string raised, string outline, string ink, string muted,
			string onTime, string delay, string cancelled)
			: this(
				Rgb.Parse(bg), Rgb.Parse(surface), Rgb.Parse(raised), Rgb.Parse(outline),
				Rgb.Parse(ink), Rgb.Parse(muted), Rgb.Parse(onTime), Rgb.Parse(delay), Rgb.Parse(cancelled))
		{
		}
	}
}
