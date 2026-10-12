using DDjourneys.Core.Services;

namespace DDjourneys.Tracking.Tests;

/// <summary>The small time-limited cache: the first tests for it (nothing used it before Tier 2).</summary>
public class TtlCacheTests
{
	[Fact]
	public void An_entry_answers_until_its_lifetime_is_over()
	{
		TtlCache<string> cache = new(TimeSpan.FromMilliseconds(80));

		cache.Set("key", "value");

		Assert.True(cache.TryGet("key", out string? value));
		Assert.Equal("value", value);

		Thread.Sleep(120);

		Assert.False(cache.TryGet("key", out string? _));
	}

	[Fact]
	public void A_missing_key_is_not_an_answer()
	{
		TtlCache<string> cache = new(TimeSpan.FromMinutes(5));

		Assert.False(cache.TryGet("nope", out string? _));
	}

	[Fact]
	public void The_same_key_is_overwritten_in_place()
	{
		TtlCache<int> cache = new(TimeSpan.FromMinutes(5));

		cache.Set("key", 1);
		cache.Set("key", 2);

		Assert.True(cache.TryGet("key", out int value));
		Assert.Equal(2, value);
	}

	[Fact]
	public void A_full_cache_is_cleared_not_evicted_one_by_one()
	{
		TtlCache<string> cache = new(TimeSpan.FromMinutes(5), 2);

		cache.Set("a", "1");
		cache.Set("b", "2");
		cache.Set("c", "3");

		Assert.False(cache.TryGet("a", out string? _));
		Assert.False(cache.TryGet("b", out string? _));
		Assert.True(cache.TryGet("c", out string? _));
	}
}
