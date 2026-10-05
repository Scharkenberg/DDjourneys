using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

/// <summary>Maps the departure monitor, route change, line and map DTOs of the VVO WebAPI to the app's models.</summary>
public static class VvoNetworkMapper
{
	public static DepartureBoard MapBoard(
		VvoDepartureResponse response,
		Location stop,
		bool isArrival)
	{
		ArgumentNullException.ThrowIfNull(response);
		ArgumentNullException.ThrowIfNull(stop);

		string stopId = stop.Id ?? string.Empty;

		return new DepartureBoard
		{
			StopName = response.Name ?? stop.Name,
			StopPlace = VvoPlaces.Resolve(response.Place ?? stop.Place),
			Departures =
				[.. response.Departures
					.Select(departure => MapDeparture(departure, stopId, isArrival))
					.OfType<Departure>()]
		};
	}

	public static Departure? MapDeparture(
		VvoDeparture departure,
		string stopId,
		bool isArrival)
	{
		DateTimeOffset? scheduled =
			departure.ScheduledTime ?? departure.RealTime;

		if (scheduled is not { } plan
			|| string.IsNullOrWhiteSpace(departure.Id))
		{
			return null;
		}

		return new Departure
		{
			Id = departure.Id,
			StopId = stopId,
			IsArrival = isArrival,
			Line =
				new TransitLine
				{
					Name = departure.LineName ?? string.Empty,
					Mode = ModeOf(departure.Mot),
					Destination = departure.Direction?.Trim(),
					DirectionId = departure.DlId
				},
			Scheduled = plan,
			Realtime = departure.RealTime,
			Platform = departure.Platform?.Name,
			PlatformKind = StopLabel.KindOf(departure.Platform?.Type),
			State = StateOf(departure.State),
			Occupancy = VvoModeMapper.MapOccupancy(departure.Occupancy),
			RouteChangeIds = [.. departure.RouteChanges.Where(id => !string.IsNullOrWhiteSpace(id))]
		};
	}

	public static IReadOnlyList<RunStop> MapRun(
		VvoRunResponse response) =>
		[.. response.Stops
			.Where(stop => !string.IsNullOrWhiteSpace(stop.Name))
			.Select(
				stop =>
				{
					(double Latitude, double Longitude)? position =
						Position(stop.Latitude, stop.Longitude);

					return new RunStop
					{
						Station =
							new Station
							{
								Id = stop.Id ?? string.Empty,
								ProviderId = VvoProviderInfo.Id,
								Name = stop.Name ?? string.Empty,
								Place = VvoPlaces.Resolve(stop.Place),
								Latitude = position?.Latitude,
								Longitude = position?.Longitude,
								Platform = stop.Platform?.Name,
								PlatformKind = StopLabel.KindOf(stop.Platform?.Type)
							},
						Position = RunPositionOf(stop.Position),
						Scheduled = stop.Time,
						Realtime = stop.RealTime,
						State = StateOf(stop.State),
						Occupancy = VvoModeMapper.MapOccupancy(stop.Occupancy)
					};
				})];

	public static DisruptionReport MapDisruptions(
		VvoRouteChangesResponse response)
	{
		Dictionary<string, DisruptionLine> lines =
			response.Lines
				.Select(MapLine)
				.OfType<DisruptionLine>()
				.GroupBy(line => line.Id)
				.ToDictionary(group => group.Key, group => group.First());

		return new DisruptionReport
		{
			Changes =
				[.. response.Changes
					.Where(change => !string.IsNullOrWhiteSpace(change.Id))
					.Select(
						change =>
							new Disruption
							{
								Id = change.Id!,
								Title = PlainText(change.Title),
								Description = PlainText(change.Description),
								DescriptionHtml = change.Description ?? string.Empty,
								IsPlanned = string.Equals(change.Type, "Scheduled", StringComparison.OrdinalIgnoreCase),
								AffectsRouting = change.TripRequestInclude,
								Published = change.PublishDate,
								Lines =
									[.. change.LineIds
										.Select(id => lines.GetValueOrDefault(id))
										.OfType<DisruptionLine>()],
								Periods =
									[.. change.ValidityPeriods
										.Select(
											period =>
												new DisruptionPeriod
												{
													Begin = period.Begin,
													End = period.End
												})]
							})],
			Banners =
				[.. response.Banners
					.Where(banner => !string.IsNullOrWhiteSpace(banner.Title))
					.Select(
						banner =>
							new NetworkBanner
							{
								Title = PlainText(banner.Title),
								Description = PlainText(banner.Description),
								DescriptionHtml = banner.Description ?? string.Empty,
								Modified = banner.ModifiedTime
							})]
		};
	}

	public static DisruptionLine? MapLine(
		VvoChangedLine line)
	{
		if (string.IsNullOrWhiteSpace(line.Id)
			|| string.IsNullOrWhiteSpace(line.Name))
		{
			return null;
		}

		return new DisruptionLine
		{
			Id = line.Id,
			Name = line.Name,
			Mode = ModeOf(line.Mot),
			Operator = line.TransportationCompany
		};
	}

	public static IReadOnlyList<StopLine> MapStopLines(
		VvoStopLinesResponse response) =>
		[.. response.Lines
			.Where(line => !string.IsNullOrWhiteSpace(line.Name))
			.Select(
				line =>
					new StopLine
					{
						Name = line.Name!,
						Mode = ModeOf(line.Mot),
						RouteChangeIds = [.. line.Changes],
						Directions =
							[.. line.Directions
								.Where(direction => !string.IsNullOrWhiteSpace(direction.Name))
								.Select(
									direction =>
										new StopLineDirection
										{
											Name = direction.Name!.Trim(),
											Timetables =
												[.. direction.TimeTables
													.Select(table => table.Name)
													.Where(name => !string.IsNullOrWhiteSpace(name))
													.Select(name => name!)]
										})]
					})];

	public static TransitMode ModeOf(
		string? mot) =>
		VvoModeMapper.MapMode(
			new VvoMot
			{
				Type = mot
			});

	public static DepartureState StateOf(
		string? state) =>
		state?.Trim().ToLowerInvariant() switch
		{
			"intime" => DepartureState.InTime,
			"delayed" => DepartureState.Delayed,
			"cancelled" => DepartureState.Cancelled,
			_ => DepartureState.Unknown
		};

	private static RunPosition RunPositionOf(
		string? position) =>
		position?.Trim().ToLowerInvariant() switch
		{
			"previous" => RunPosition.Previous,
			"current" => RunPosition.Current,
			"next" => RunPosition.Next,
			"onward" => RunPosition.Onward,
			_ => RunPosition.Unknown
		};

	/// <summary>A GK4 pair as the API names it (first "latitude", second "longitude") to WGS84.</summary>
	public static (double Latitude, double Longitude)? Position(
		double first,
		double second) =>
		VvoCoordinateConverter.TryFromPointFields(
			first.ToString("F0", CultureInfo.InvariantCulture),
			second.ToString("F0", CultureInfo.InvariantCulture),
			out (double Latitude, double Longitude) result)
			? result
			: null;

	/// <summary>The provider sends HTML in titles and descriptions; the app shows text.</summary>
	public static string PlainText(
		string? html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return string.Empty;
		}

		string text = LineBreaksRx.Replace(html, "\n");

		text = TagsRx.Replace(text, string.Empty);
		text = WebUtility.HtmlDecode(text);
		text = text.Replace(' ', ' ');
		text = SpacesRx.Replace(text, " ");
		text = NewlineSpacesRx.Replace(text, "\n");
		text = BlankLinesRx.Replace(text, "\n\n");

		return text.Trim();
	}

	// Plain static fields: the regex source generator does not run in this solution.
	private static readonly Regex LineBreaksRx = new(@"<\s*(br\s*/?|/p|/div|/li|/h\d)\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex TagsRx = new(@"<[^>]+>", RegexOptions.Compiled);
	private static readonly Regex SpacesRx = new(@"[ \t\r\f\v]+", RegexOptions.Compiled);
	private static readonly Regex NewlineSpacesRx = new(@" *\n *", RegexOptions.Compiled);
	private static readonly Regex BlankLinesRx = new(@"\n{3,}", RegexOptions.Compiled);
}
