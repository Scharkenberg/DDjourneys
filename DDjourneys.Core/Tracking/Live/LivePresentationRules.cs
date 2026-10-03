namespace DDjourneys.Core.Tracking.Live;

/// <summary>Platform-neutral decisions of surfaces that must refresh their own countdown (unit-testable).</summary>
public static class LivePresentationRules
{
	private static readonly TimeSpan MinDelay = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(50);

	/// <summary>Everything a bound update cannot change; when it differs, the notification must be replaced.</summary>
	public static string LayoutKey(LiveJourneyContent content) =>
		string.Join(
			'|',
			content.PlanId,
			content.Title,
			content.Text,
			content.SubText,
			content.ShortText,
			content.Phase,
			content.Ongoing,
			string.Join(',', content.Segments));

	/// <summary>Position along the summed segment lengths, 0..1.</summary>
	public static double ProgressValue(LiveJourneyContent content)
	{
		long total = 0;

		foreach (int segment in content.Segments)
		{
			total += Math.Max(segment, 0);
		}

		return total <= 0
			? 0
			: Math.Clamp((double)content.Position / total, 0, 1);
	}

	/// <summary>Time until the next event; negative once it has passed. Null without a target.</summary>
	public static TimeSpan? Remaining(LiveJourneyContent content, DateTimeOffset now) =>
		content.When is { } when ? when - now : null;

	/// <summary>Whole minutes shown for <paramref name="remaining"/>: rounded away from zero, 0 only at exactly zero.</summary>
	public static int Minutes(TimeSpan remaining) =>
		remaining >= TimeSpan.Zero
			? (int)Math.Ceiling(remaining.TotalMinutes)
			: -(int)Math.Ceiling(-remaining.TotalMinutes);

	/// <summary>How long until <see cref="Minutes"/> changes (clamped to 1..60 s, so sleep or resume resyncs).</summary>
	public static TimeSpan NextTickDelay(TimeSpan remaining)
	{
		TimeSpan delay;

		if (remaining > TimeSpan.Zero)
		{
			int minutes = (int)Math.Ceiling(remaining.TotalMinutes);

			delay = remaining - TimeSpan.FromMinutes(minutes - 1);
		}
		else
		{
			TimeSpan elapsed = -remaining;
			int next = (int)Math.Floor(elapsed.TotalMinutes) + 1;

			delay = TimeSpan.FromMinutes(next) - elapsed;
		}

		return Clamp(delay + Margin);
	}

	private static TimeSpan Clamp(TimeSpan value) =>
		value < MinDelay ? MinDelay : value > MaxDelay ? MaxDelay : value;
}