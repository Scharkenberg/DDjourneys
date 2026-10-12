using DDjourneys.Core.Theming;

namespace DDjourneys.Tracking.Tests;

/// <summary>
/// The colour-vision line-colour sets (B11): every remap keeps all transport modes pairwise distinct under its
/// deficiency, stays reasonably distinct under the other two, and keeps its white-text contrast. The standard set
/// is the Tokens.xaml truth - the remaps only ever replace it wholesale.
/// </summary>
public sealed class CvdPaletteTests
{
	private static readonly ColourVisionSet[] Deficiencies =
	[
		ColourVisionSet.Deuteranopia,
		ColourVisionSet.Protanopia,
		ColourVisionSet.Tritanopia
	];

	[Fact]
	public void Every_set_covers_every_mode_exactly_once()
	{
		foreach (ColourVisionSet set in Enum.GetValues<ColourVisionSet>())
		{
			IReadOnlyList<Rgb> colors = ModeColorSets.ColorsOf(set);

			Assert.Equal(ModeColorSets.Modes.Count, colors.Count);

			foreach (Rgb color in colors)
			{
				Assert.NotEqual(Rgb.Black, color);
			}
		}
	}

	[Fact]
	public void The_standard_set_is_the_tokens_xaml_truth()
	{
		string[] expected =
		[
			"#B3261E", "#6B3FA0", "#1F7A4D", "#4B5B66",
			"#1D5FA8", "#8A5A00", "#5B6B74", "#0F766E"
		];

		IReadOnlyList<Rgb> standard = ModeColorSets.Standard;

		for (int i = 0; i < expected.Length; i++)
		{
			Assert.Equal(expected[i], standard[i].ToHex());
		}
	}

	[Theory]
	[InlineData(ColourVisionSet.Deuteranopia, 12.0)]
	[InlineData(ColourVisionSet.Protanopia, 12.0)]
	[InlineData(ColourVisionSet.Tritanopia, 12.0)]
	public void A_remap_keeps_every_pair_of_modes_distinct_under_its_deficiency(
		ColourVisionSet set,
		double margin)
	{
		IReadOnlyList<Rgb> colors = ModeColorSets.ColorsOf(set);

		for (int i = 0; i < colors.Count; i++)
		{
			for (int j = i + 1; j < colors.Count; j++)
			{
				double difference = ColourVisionMath.Difference(colors[i], colors[j], set);

				Assert.True(
					difference >= margin,
					$"{ModeColorSets.Modes[i]} vs {ModeColorSets.Modes[j]}: {difference:F1} Lab units under {set}");
			}
		}
	}

	[Theory]
	[InlineData(ColourVisionSet.Deuteranopia)]
	[InlineData(ColourVisionSet.Protanopia)]
	[InlineData(ColourVisionSet.Tritanopia)]
	public void A_remap_stays_distinct_under_the_other_deficiencies_too(ColourVisionSet set)
	{
		IReadOnlyList<Rgb> colors = ModeColorSets.ColorsOf(set);

		foreach (ColourVisionSet other in Deficiencies.Where(d => d != set))
		{
			for (int i = 0; i < colors.Count; i++)
			{
				for (int j = i + 1; j < colors.Count; j++)
				{
					double difference = ColourVisionMath.Difference(colors[i], colors[j], other);

					Assert.True(
						difference >= 10.0,
						$"{ModeColorSets.Modes[i]} vs {ModeColorSets.Modes[j]}: {difference:F1} Lab units under {other} (set {set})");
				}
			}
		}
	}

	[Theory]
	[InlineData(ColourVisionSet.Deuteranopia)]
	[InlineData(ColourVisionSet.Protanopia)]
	[InlineData(ColourVisionSet.Tritanopia)]
	public void Every_colour_of_a_remap_keeps_white_text_readable(ColourVisionSet set)
	{
		foreach (Rgb color in ModeColorSets.ColorsOf(set))
		{
			Assert.True(
				ColourVisionMath.ContrastWithWhite(color) >= 4.5,
				$"{color.ToHex()} carries {ColourVisionMath.ContrastWithWhite(color):F2}:1 against white");
		}
	}

	[Fact]
	public void Ids_round_trip_and_unknown_falls_back_to_standard()
	{
		foreach (string id in ModeColorSets.Ids)
		{
			Assert.Equal(id, ModeColorSets.IdOf(ModeColorSets.Of(id)));
		}

		Assert.Equal(ModeColorSets.StandardId, ModeColorSets.Normalize(null));
		Assert.Equal(ModeColorSets.StandardId, ModeColorSets.Normalize("no-such-set"));
	}

	[Fact]
	public void The_text_size_steps_scale_the_tokens_and_stay_sane()
	{
		Assert.Equal(1.0, TextSizeProfile.FactorOf(TextSizeProfile.NormalId));
		Assert.True(TextSizeProfile.FactorOf(TextSizeProfile.LargeId) > 1.0);
		Assert.True(TextSizeProfile.FactorOf(TextSizeProfile.LargestId) > TextSizeProfile.FactorOf(TextSizeProfile.LargeId));
		Assert.True(TextSizeProfile.FactorOf(TextSizeProfile.LargestId) <= 1.4);

		Assert.Equal(7, TextSizeProfile.Tokens.Count);

		foreach ((string key, double size) in TextSizeProfile.Tokens)
		{
			Assert.True(size >= 10 && size <= 30, key);
		}

		Assert.Equal(TextSizeProfile.NormalId, TextSizeProfile.Normalize(null));
		Assert.Equal(TextSizeProfile.NormalId, TextSizeProfile.Normalize("giant"));
	}
}
