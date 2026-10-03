namespace DDjourneys.Core.Tracking;

public enum NoticeKind
{
	Information,
	ConnectionRisk,
	Cancellation
}

/// <summary>Whether a notice may still be shown, and until when (null = until the journey is over).</summary>
public readonly record struct NoticeVisibility(bool IsVisible, DateTimeOffset? ExpiresAt)
{
	public static NoticeVisibility Hidden { get; } = new(false, null);
}

/// <summary>
/// How long the newest notice of a followed journey stays on screen. A newer notice replaces an older one
/// by itself; this decides when the newest one is outdated. Evaluated on every render pass of the tracker
/// (every few seconds), so a notice disappears shortly after it expires.
/// </summary>
public static class NoticePolicy
{
	/// <summary>General information (diversions, works) stays relevant for a ride's length.</summary>
	public static TimeSpan InformationAge { get; } = TimeSpan.FromMinutes(30);

	/// <summary>A connection warning is only valid while the connection is still at risk; without a live risk it fades fast.</summary>
	public static TimeSpan RiskAge { get; } = TimeSpan.FromMinutes(10);

	/// <param name="time">When the notice was issued; null if unknown (then it never ages out).</param>
	/// <param name="journeyOver">Arrived, or no longer followed.</param>
	/// <param name="liveRisk">The real-time check currently finds an unreachable change.</param>
	public static NoticeVisibility Evaluate(
		NoticeKind kind,
		DateTimeOffset? time,
		bool journeyOver,
		bool liveRisk,
		DateTimeOffset now)
	{
		if (journeyOver)
		{
			return NoticeVisibility.Hidden;
		}

		switch (kind)
		{
			case NoticeKind.Cancellation:
				return new NoticeVisibility(true, null);

			case NoticeKind.ConnectionRisk:
				if (liveRisk)
				{
					return new NoticeVisibility(true, null);
				}

				return WithAge(time, RiskAge, now);

			default:
				return WithAge(time, InformationAge, now);
		}
	}

	private static NoticeVisibility WithAge(DateTimeOffset? time, TimeSpan age, DateTimeOffset now)
	{
		if (time is not { } issued)
		{
			return new NoticeVisibility(true, null);
		}

		DateTimeOffset expires = issued + age;

		return now <= expires
			? new NoticeVisibility(true, expires)
			: NoticeVisibility.Hidden;
	}
}
