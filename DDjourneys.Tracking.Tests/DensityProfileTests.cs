using DDjourneys.Core.Theming;
using Xunit;

namespace DDjourneys.Tracking.Tests;

public sealed class DensityProfileTests
{
	[Fact]
	public void Normal_is_a_touch_tighter_than_the_authored_values()
	{
		DensityProfile normal = DensityProfile.Normal;

		Assert.Equal(49, normal.Hit(52));
		Assert.Equal(42, normal.Hit(44));
		Assert.Equal(7, normal.PaddingVertical(8));
		Assert.Equal(11, normal.PaddingHorizontal(12));
	}

	[Theory]
	[InlineData(52, 42, 49, 58)]
	[InlineData(48, 38, 46, 54)]
	[InlineData(44, 35, 42, 49)]
	[InlineData(64, 51, 61, 72)]
	[InlineData(40, 34, 38, 45)]
	[InlineData(38, 34, 36, 43)]
	[InlineData(36, 34, 34, 40)]
	[InlineData(28, 28, 27, 31)]
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
		Assert.Equal(14, DensityProfile.Touch.PaddingVertical(10));
		Assert.Equal(8, DensityProfile.Compact.PaddingHorizontal(10));
		Assert.Equal(13, DensityProfile.Touch.PaddingHorizontal(12));
	}
}
