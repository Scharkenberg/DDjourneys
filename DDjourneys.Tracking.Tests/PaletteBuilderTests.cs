using DDjourneys.Core.Theming;

namespace DDjourneys.Tracking.Tests;

public sealed class PaletteBuilderTests
{
	public static TheoryData<string> Seeds =>
		new()
		{
			"#000000", "#FFFFFF", "#808080", "#FF0000", "#00FF00", "#0000FF", "#FFFF00",
			"#6750A4", "#D0BCFF", "#1B6EF3", "#0B6E8A", "#FFD400", "#B91C1C", "#5CC0DA"
		};

	[Theory]
	[MemberData(nameof(Seeds))]
	public void Every_combination_stays_legible(string hex)
	{
		Rgb seed = Rgb.Parse(hex);

		foreach (bool dark in new[] { false, true })
		{
			foreach (bool solarized in new[] { false, true })
			{
				foreach (bool black in dark ? new[] { false, true } : new[] { false })
				foreach (bool tint in new[] { false, true })
				{
					PaletteColors p = PaletteBuilder.Build(seed, dark, solarized, black, tint);
					string label = $"{hex} dark={dark} sol={solarized} black={black} tint={tint}";

					foreach (Rgb bg in new[] { p.Bg, p.Surface })
					{
						Assert.True(Rgb.Contrast(p.Ink, bg) >= 7.0, $"ink {label}");
						Assert.True(Rgb.Contrast(p.InkMuted, bg) >= 4.5, $"muted {label}");
						Assert.True(Rgb.Contrast(p.Accent, bg) >= 4.5, $"accent {label}");
						Assert.True(Rgb.Contrast(p.OnTime, bg) >= 4.5, $"ontime {label}");
						Assert.True(Rgb.Contrast(p.Delay, bg) >= 4.5, $"delay {label}");
						Assert.True(Rgb.Contrast(p.Cancelled, bg) >= 4.5, $"cancelled {label}");
					}

					Assert.True(Rgb.Contrast(p.OnAccent, p.Accent) >= 4.5, $"onaccent {label}");
					Assert.True(Rgb.Contrast(p.Ink, p.AccentSoft) >= 4.5, $"soft {label}");
					Assert.True(Rgb.Contrast(p.Accent, p.AccentSoft) >= 2.5, $"accent/soft {label}");
				}
			}
		}
	}

	[Fact]
	public void Pure_black_makes_bg_and_surface_black_and_keeps_the_old_surface_as_raised()
	{
		PaletteColors plain = PaletteBuilder.Build(Rgb.Parse("#5CC0DA"), dark: true, solarized: false);
		PaletteColors black = PaletteBuilder.Build(Rgb.Parse("#5CC0DA"), dark: true, solarized: false, pureBlack: true);

		Assert.Equal(Rgb.Black, black.Bg);
		Assert.Equal(Rgb.Black, black.Surface);
		Assert.Equal(plain.Surface, black.Raised);
	}

	[Fact]
	public void Pure_black_is_ignored_for_light_palettes()
	{
		PaletteColors p = PaletteBuilder.Build(Rgb.Parse("#0B6E8A"), dark: false, solarized: false, pureBlack: true);

		Assert.NotEqual(Rgb.Black, p.Bg);
	}

	[Fact]
	public void A_legible_seed_is_kept()
	{
		Rgb seed = Rgb.Parse("#0B6E8A");

		Assert.Equal(seed, PaletteBuilder.Build(seed, dark: false, solarized: false).Accent);
	}

	[Fact]
	public void Hex_round_trips()
	{
		Assert.Equal("#0B6E8A", Rgb.Parse("0b6e8a").ToHex());
		Assert.Equal(21.0, Rgb.Contrast(Rgb.Black, Rgb.White), 3);
	}
}
