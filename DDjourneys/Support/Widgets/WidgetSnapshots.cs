using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Widgets;
using DDjourneys.Localization;

namespace DDjourneys.Support.Widgets;

/// <summary>
/// Turns what the providers answered into the rows a widget draws. Clock times are shown, never "in 4 minutes":
/// a widget is refreshed rarely, so a relative time would be wrong a few minutes later.
/// </summary>
public static class WidgetSnapshots
{
	public static WidgetRow ForDeparture(Departure departure, IUiStrings strings)
	{
		ArgumentNullException.ThrowIfNull(departure);
		ArgumentNullException.ThrowIfNull(strings);

		string? delay = Format.Delay(departure.Delay);

		return new WidgetRow
		{
			Chip = LineText(departure.Line),
			Mode = departure.Line.Mode,
			Main = departure.Line.Destination ?? string.Empty,
			Sub = Platform(departure.Platform, departure.PlatformKind, strings.Journey),
			Time = Format.Time(departure.Effective),
			Delay = departure.IsCancelled ? strings.Journey.Cancelled : delay ?? string.Empty,
			DelayLevel =
				departure.IsCancelled
					? WidgetDelay.Cancelled
					: departure.Delay is { } late && late >= TimeSpan.FromMinutes(1)
						? WidgetDelay.Late
						: WidgetDelay.None,
			At = departure.Effective
		};
	}

	public static WidgetRow ForJourney(Journey journey, IUiStrings strings)
	{
		ArgumentNullException.ThrowIfNull(journey);
		ArgumentNullException.ThrowIfNull(strings);

		JourneyStrings text = strings.Journey;

		JourneyLeg[] rides = [.. journey.Legs.Where(leg => leg.IsRide)];
		JourneyLeg? first = rides.FirstOrDefault();

		int transfers = journey.TransferCount;

		string transfersText =
			transfers switch
			{
				0 => text.Direct,
				1 => text.OneTransfer,
				_ => string.Format(CultureInfo.CurrentCulture, text.MultipleTransfers, transfers)
			};

		string lines =
			string.Join(
				" ",
				rides
					.Select(leg => leg.Line?.Name)
					.Where(name => !string.IsNullOrWhiteSpace(name))
					.Distinct());

		TimeSpan? delay =
			first is { RealtimeDeparture: { } real, ScheduledDeparture: { } plan }
				? real - plan
				: null;

		// A journey that starts on foot: the stop to walk to (where the vehicle is boarded) is what matters most, so it
		// leads the row; the walk, the arrival and the rest follow in the second line.
		JourneyLeg? firstLeg = journey.Legs.Count > 0 ? journey.Legs[0] : null;

		bool walksFirst =
			first is not null
			&& firstLeg is { IsRide: false }
			&& !ReferenceEquals(firstLeg, first);

		string arrival = $"→ {Format.TimeOrDash(journey.Arrival)}";

		string walk =
			walksFirst
			&& firstLeg!.ScheduledDeparture is { } walkStart
			&& firstLeg.ScheduledArrival is { } walkEnd
				? $"{Format.TransportMode(TransitMode.Walk)} {Format.Duration(walkEnd - walkStart)}"
				: string.Empty;

		return new WidgetRow
		{
			Chip = first?.Line is { } line ? LineText(line) : Format.TransportMode(TransitMode.Walk),
			Mode = first?.Mode ?? TransitMode.Walk,
			Main = walksFirst
				? StopLabel.NameFor(first!.From.Name, first.From.Place)
				: arrival,
			Sub = walksFirst
				? string.Join(" · ", new[] { walk, arrival, transfersText, lines }.Where(part => part.Length > 0))
				: string.Join(" · ", new[] { Format.Duration(journey.Duration), transfersText, lines }.Where(part => part.Length > 0)),
			Time = Format.TimeOrDash(journey.Departure),
			Delay = journey.IsImpossible ? text.Cancelled : Format.Delay(delay) ?? string.Empty,
			DelayLevel =
				journey.IsImpossible
					? WidgetDelay.Cancelled
					: delay is { } late && late >= TimeSpan.FromMinutes(1)
						? WidgetDelay.Late
						: WidgetDelay.None,
			At = journey.Departure,
			Arrival = Format.TimeOrDash(journey.Arrival),
			Duration = Format.Duration(journey.Duration),
			Transfers = transfersText,
			Lines = string.Join(
				" \u203a ",
				rides
					.Select(leg => leg.Line?.Name)
					.Where(name => !string.IsNullOrWhiteSpace(name))
					.Select(name => name!)),
			Lead = walksFirst && walk.Length > 0
				? $"{walk} \u2192 {StopLabel.NameFor(first!.From.Name, first.From.Place)}"
				: string.Empty
		};
	}

	public static WidgetRow ForStop(NearbyStop stop, WidgetStrings strings)
	{
		ArgumentNullException.ThrowIfNull(stop);
		ArgumentNullException.ThrowIfNull(strings);

		return new WidgetRow
		{
			Chip = Meters(stop.DistanceMeters, strings),
			Main = stop.Stop.Name,
			Sub = stop.Stop.Place ?? string.Empty
		};
	}

	public static WidgetRow ForHeader(NearbyStop stop, WidgetStrings strings)
	{
		ArgumentNullException.ThrowIfNull(stop);
		ArgumentNullException.ThrowIfNull(strings);

		return new WidgetRow
		{
			Kind = WidgetRowKind.Header,
			Main = stop.Stop.Name,
			Sub = stop.Stop.Place ?? string.Empty,
			Time = Meters(stop.DistanceMeters, strings)
		};
	}

	public static string Meters(int meters, WidgetStrings strings) =>
		string.Format(CultureInfo.CurrentCulture, strings.Meters, meters);

	private static string LineText(TransitLine line) =>
		string.IsNullOrWhiteSpace(line.Name)
			? Format.TransportMode(line.Mode)
			: line.Name;

	private static string Platform(string? platform, PlatformKind kind, JourneyStrings text) =>
		string.IsNullOrWhiteSpace(platform)
			? string.Empty
			: $"{(kind == PlatformKind.Railtrack ? text.Track : text.Platform)} {platform.Trim()}";
}
