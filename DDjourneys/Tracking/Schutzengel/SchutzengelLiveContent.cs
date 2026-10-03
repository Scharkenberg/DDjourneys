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
	/// <summary>
	/// What the notification says is what the traveller has to DO next: board, get off, change, walk. It therefore
	/// only changes when that changes (new ride or walk, a different time or platform, a delay, a notice, a risk),
	/// never with the passing of intermediate stops. Countdowns run natively via <c>When</c> and need no updates.
	/// </summary>
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
		int position = snapshot.Position;

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
				position = snapshot.EpisodeStartPosition;
				break;

			case TrackingPhase.AtInterchange:
			{
				TripEpisode? ride = snapshot.NextRide;
				string here = snapshot.CurrentStop ?? snapshot.NextStop ?? origin;
				string? there = ride?.From.Name;

				// A real walk to another stop is named as such; a change at the same stop is just "change".
				title =
					there is { Length: > 0 } && !string.Equals(there.Trim(), here.Trim(), StringComparison.OrdinalIgnoreCase)
						? string.Format(CultureInfo.CurrentCulture, strings.CourseWalk, there)
						: string.Format(CultureInfo.CurrentCulture, strings.NotifChangeTitle, here);

				text =
					ride is not null
						? Boarding(ride, strings)
						: string.Format(CultureInfo.CurrentCulture, strings.NotifChangeText, Format.TimeOrDash(snapshot.NextStopTime));
				when = ride?.From.Effective ?? snapshot.NextStopTime;
				position = snapshot.EpisodeStartPosition;
				break;
			}

			case TrackingPhase.InProgress:
				title =
					snapshot.MotName is { Length: > 0 } line
						&& snapshot.Direction is { Length: > 0 } direction
						? string.Format(CultureInfo.CurrentCulture, strings.NotifRiding, line, direction)
						: route;
				text =
					snapshot.EpisodeEnd is { Name.Length: > 0 } end
						? string.Format(
							CultureInfo.CurrentCulture,
							strings.NotifGetOff,
							WithPlatform(end.Name, end.Platform, end.PlatformIsTrack, strings),
							Format.TimeOrDash(end.Effective))
						: route;
				when = snapshot.EpisodeEnd?.Effective;
				position = snapshot.EpisodeStartPosition;
				break;

			default:
				title =
					string.Format(
						CultureInfo.CurrentCulture,
						strings.NotifStartsAt,
						Format.TimeOrDash(snapshot.Start));
				text = route;
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
			Math.Clamp(position, 0, total),
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

	private static string Boarding(TripEpisode ride, TrackingStrings strings)
	{
		string line = ride.MotName is { Length: > 0 } name ? name : ride.From.Name;

		return string.Format(
			CultureInfo.CurrentCulture,
			strings.NotifBoard,
			WithPlatform(line, ride.From.Platform, ride.From.PlatformIsTrack, strings),
			Format.TimeOrDash(ride.From.Effective));
	}

	private static string WithPlatform(string text, string? platform, bool isTrack, TrackingStrings strings) =>
		string.IsNullOrWhiteSpace(platform)
			? text
			: $"{text}, {string.Format(CultureInfo.CurrentCulture, isTrack ? strings.CourseTrack : strings.CoursePlatform, platform)}";
}
