using DDjourneys.Core.Theming;
using Xunit;

namespace DDjourneys.Tracking.Tests;

public sealed class DensityProfileTests
{
	[Fact]
	public void Normal_keeps_every_authored_value()
	{
		DensityProfile normal = DensityProfile.Normal;

		Assert.Equal(52, normal.Hit(52));
		Assert.Equal(44, normal.Hit(44));
		Assert.Equal(8, normal.PaddingVertical(8));
		Assert.Equal(12, normal.PaddingHorizontal(12));
	}

	[Theory]
	[InlineData(52, 44, 52, 62)]
	[InlineData(48, 41, 48, 58)]
	[InlineData(44, 37, 44, 53)]
	[InlineData(64, 54, 64, 77)]
	[InlineData(40, 36, 40, 48)]
	[InlineData(38, 36, 38, 46)]
	[InlineData(36, 36, 36, 43)]
	[InlineData(28, 28, 28, 34)]
	public void Hit_sizes_per_density(double authored, double compact, double normal, double touch)
	{
		Assert.Equal(compact, DensityProfile.Compact.Hit(authored));
		Assert.Equal(normal, DensityProfile.Normal.Hit(authored));
		Assert.Equal(touch, DensityProfile.Touch.Hit(authored));
	}

	[Fact]
	public void Compact_never_goes_below_the_floor_or_the_authored_size()
	{
		foreach (double authored in new double[] { 20, 28, 30, 36, 38, 40, 44, 48, 52, 64 })
		{
			double compact = DensityProfile.Compact.Hit(authored);

			Assert.True(compact >= Math.Min(authored, DensityProfile.CompactHitFloor));
			Assert.True(compact <= authored);
		}
	}

	[Fact]
	public void Touch_targets_reach_48_where_normal_is_44_or_more()
	{
		Assert.True(DensityProfile.Touch.Hit(44) >= 48);
	}

	[Fact]
	public void Tokens_grow_monotonically_with_density()
	{
		(double c, double n, double t)[] rows =
		[
			(DensityProfile.Compact.SpaceXS, DensityProfile.Normal.SpaceXS, DensityProfile.Touch.SpaceXS),
			(DensityProfile.Compact.SpaceS, DensityProfile.Normal.SpaceS, DensityProfile.Touch.SpaceS),
			(DensityProfile.Compact.SpaceM, DensityProfile.Normal.SpaceM, DensityProfile.Touch.SpaceM),
			(DensityProfile.Compact.SpaceL, DensityProfile.Normal.SpaceL, DensityProfile.Touch.SpaceL),
			(DensityProfile.Compact.SpaceXL, DensityProfile.Normal.SpaceXL, DensityProfile.Touch.SpaceXL),
			(DensityProfile.Compact.CardPadding, DensityProfile.Normal.CardPadding, DensityProfile.Touch.CardPadding),
			(DensityProfile.Compact.PagePaddingVertical, DensityProfile.Normal.PagePaddingVertical, DensityProfile.Touch.PagePaddingVertical),
			(DensityProfile.Compact.FieldPaddingVertical, DensityProfile.Normal.FieldPaddingVertical, DensityProfile.Touch.FieldPaddingVertical),
			(DensityProfile.Compact.SectionGap, DensityProfile.Normal.SectionGap, DensityProfile.Touch.SectionGap)
		];

		foreach ((double c, double n, double t) in rows)
		{
			Assert.True(c < n && n < t);
		}
	}

	[Theory]
	[InlineData("compact")]
	[InlineData("NORMAL")]
	[InlineData("touch")]
	public void Find_is_case_insensitive(string id)
	{
		Assert.Equal(id.ToLowerInvariant(), DensityProfile.Find(id).Id);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("huge")]
	public void Find_falls_back_to_normal(string? id)
	{
		Assert.Same(DensityProfile.Normal, DensityProfile.Find(id));
	}

	[Fact]
	public void Padding_rounds_to_whole_units()
	{
		Assert.Equal(5, DensityProfile.Compact.PaddingVertical(10));
		Assert.Equal(15, DensityProfile.Touch.PaddingVertical(10));
		Assert.Equal(9, DensityProfile.Compact.PaddingHorizontal(10));
		Assert.Equal(13, DensityProfile.Touch.PaddingHorizontal(12));
	}
}
