using System.Globalization;
using DDjourneys.Core.Tracking;
using DDjourneys.Core.Tracking.Live;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Tracking.Schutzengel;

/// <summary>
/// Turns a trip snapshot into the platform-neutral <see cref="LiveJourneyContent"/>. Every
/// <see cref="ILiveJourneySurface"/> renders the same content, so wording and countdown logic
/// live here once.
/// </summary>
internal static class SchutzengelLiveContent
{
	public static LiveJourneyContent Create(
		string planId,
		TripSnapshot snapshot,
		TrackingPhase phase,
		string? notice,
		DateTimeOffset now,
		TrackingStrings strings)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(strings);

		string origin = snapshot.Origin ?? string.Empty;
		string destination = snapshot.Destination ?? string.Empty;
		string route = string.Format(CultureInfo.CurrentCulture, strings.NotifRoute, origin, destination);

		string? delay =
			snapshot.Delay >= TimeSpan.FromMinutes(1)
				? string.Format(
					CultureInfo.CurrentCulture,
					strings.NotifDelay,
					(int)Math.Round(snapshot.Delay.TotalMinutes))
				: null;

		string title;
		string text;
		string? shortText = null;
		bool ongoing = true;
		DateTimeOffset? when = null;

		switch (phase)
		{
			case TrackingPhase.Arrived:
				title = strings.NotifArrivedTitle;
				text = destination.Length > 0 ? destination : route;
				ongoing = false;
				break;

			case TrackingPhase.Cancelled:
				title = strings.NotifCancelledTitle;
				text = string.IsNullOrWhiteSpace(notice) ? route : notice;
				ongoing = false;
				break;

			case TrackingPhase.AtRisk:
				title = strings.NotifRiskTitle;
				text =
					snapshot.Risk is { } risk
						? string.Format(
							CultureInfo.CurrentCulture,
							risk.Missed ? strings.NotifMissedText : strings.NotifTightText,
							risk.Station)
						: string.IsNullOrWhiteSpace(notice)
							? route
							: notice;
				shortText = "!";
				break;

			case TrackingPhase.AtInterchange:
				title =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifChangeTitle,
						snapshot.CurrentStop ?? snapshot.NextStop ?? origin);
				text =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifChangeText,
						Format.TimeOrDash(snapshot.NextStopTime));
				shortText = Countdown(snapshot.NextStopTime, now, strings);
				when = snapshot.NextStopTime;
				break;

			case TrackingPhase.InProgress:
				title =
					snapshot.MotName is { Length: > 0 } line
						&& snapshot.Direction is { Length: > 0 } direction
						? string.Format(CultureInfo.CurrentCulture, strings.NotifRiding, line, direction)
						: route;
				text =
					snapshot.NextStop is { Length: > 0 } next
						? string.Format(
							CultureInfo.CurrentCulture,
							strings.NotifNext,
							next,
							Format.TimeOrDash(snapshot.NextStopTime))
						: route;
				shortText = Countdown(snapshot.NextStopTime, now, strings);
				when = snapshot.NextStopTime;
				break;

			default:
				title =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifStartsAt,
						Format.TimeOrDash(snapshot.Start));
				text = route;
				shortText = Countdown(snapshot.Start, now, strings);
				when = snapshot.Start;
				break;
		}

		int[] segments = [.. snapshot.SegmentLengths.Select(length => Math.Max(1, length))];
		int total = segments.Sum();

		return new LiveJourneyContent(
			planId,
			title,
			text,
			delay,
			shortText,
			segments,
			snapshot.SegmentIndividual,
			Math.Clamp(snapshot.Position, 0, total),
			phase,
			ongoing,
			when);
	}

	/// <summary>Content for the service notification while journeys wait for their start.</summary>
	public static LiveJourneyContent Waiting(WatchedJourney journey, TrackingStrings strings)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(strings);

		return new LiveJourneyContent(
			journey.PlanId,
			string.Format(
				CultureInfo.CurrentCulture,
				strings.NotifStartsAt,
				Format.TimeOrDash(journey.Departure)),
			string.Format(
				CultureInfo.CurrentCulture,
				strings.NotifRoute,
				journey.Origin,
				journey.Destination),
			null,
			null,
			[],
			[],
			0,
			TrackingPhase.Planned,
			true,
			journey.Departure);
	}

	public static LiveJourneyContent Monitoring(TrackingStrings strings) =>
		new(
			string.Empty,
			strings.NotifMonitoring,
			string.Empty,
			null,
			null,
			[],
			[],
			0,
			TrackingPhase.Planned,
			true);

	private static string? Countdown(DateTimeOffset? target, DateTimeOffset now, TrackingStrings strings)
	{
		if (target is not { } time)
		{
			return null;
		}

		double minutes = (time - now).TotalMinutes;

		if (minutes <= 0)
		{
			return strings.NotifNow;
		}

		return minutes < 100
			? string.Format(CultureInfo.CurrentCulture, strings.NotifMinutes, (int)Math.Ceiling(minutes))
			: null;
	}
}
