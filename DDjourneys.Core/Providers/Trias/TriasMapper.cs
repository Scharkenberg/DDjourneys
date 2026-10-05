using System.Globalization;
using System.Xml.Linq;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>The stops of a run as they came with a stop event; kept in <c>Departure.ProviderData</c>.</summary>
internal sealed record TriasRunData(
	IReadOnlyList<RunStop> Stops,
	string? JourneyRef = null,
	string? OperatingDayRef = null);

/// <summary>TRIAS XML to the app's models. Elements are read by local name and every field is optional.</summary>
internal static class TriasMapper
{
	// ---------- Places ----------

	public static IReadOnlyList<Location> MapLocations(XDocument document) =>
		[.. document
			.Deep("LocationResult")
			.Select(MapLocation)
			.OfType<Location>()];

	private static Location? MapLocation(XElement result)
	{
		XElement? location = result.Child("Location") ?? result;

		XElement? stopPlace = location.Child("StopPlace");
		XElement? stopPoint = location.Child("StopPoint");
		XElement? address = location.Child("Address");
		XElement? poi = location.Child("PointOfInterest");

		string? id;
		string? name;
		PlaceKind kind;

		if (stopPlace is not null)
		{
			id = stopPlace.ChildText("StopPlaceRef");
			name = stopPlace.ChildLabel("StopPlaceName");
			kind = PlaceKind.Stop;
		}
		else if (stopPoint is not null)
		{
			id = stopPoint.ChildText("StopPointRef");
			name = stopPoint.ChildLabel("StopPointName");
			kind = PlaceKind.Stop;
		}
		else if (address is not null)
		{
			id = address.ChildText("AddressCode");
			name = address.ChildLabel("AddressName");
			kind = PlaceKind.Address;
		}
		else if (poi is not null)
		{
			id = poi.ChildText("PointOfInterestCode");
			name = poi.ChildLabel("PointOfInterestName");
			kind = PlaceKind.Poi;
		}
		else
		{
			return null;
		}

		name ??= location.ChildLabel("LocationName");

		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		XElement? position = location.Child("GeoPosition");

		return new Location
		{
			Id = id,
			ProviderId = TriasProviderInfo.Id,
			Name = name,
			Kind = kind,
			Place = location.ChildLabel("LocalityName"),
			Latitude = position.Child("Latitude").Number(),
			Longitude = position.Child("Longitude").Number()
		};
	}

	// ---------- Stops, modes ----------

	/// <summary>
	/// A DHID names a platform with five parts ("de:14612:28:2:3"); its first three are the stop place
	/// ("de:14612:28"). Journey stops are identified by the place, so two legs at different platforms of one
	/// station are one stop (the platform is a detail), and the place id is what the requests take.
	/// </summary>
	private static string StationId(string? reference)
	{
		if (string.IsNullOrWhiteSpace(reference))
		{
			return string.Empty;
		}

		string[] parts = reference.Split(':');

		return parts.Length > 3
			&& parts[0].Length == 2
			&& parts[1].All(char.IsAsciiDigit)
			&& parts[2].All(char.IsAsciiDigit)
				? string.Join(':', parts.Take(3))
				: reference;
	}

	private static Station MapStop(XElement? call, string idName, string nameName, string? platform, PlatformKind platformKind) =>
		new()
		{
			Id = StationId(call.ChildText(idName)),
			ProviderId = TriasProviderInfo.Id,
			Name = call.ChildLabel(nameName) ?? call.ChildText(idName) ?? string.Empty,
			Platform = platform,
			PlatformKind = platformKind
		};

	/// <summary>
	/// Vehicle type of a service. TRIAS names it in <c>Mode/PtMode</c>; servers differ in where they put it and
	/// some leave it out, so the submode elements, the mode and product-category texts and finally the line name
	/// are tried before giving up.
	/// </summary>
	private static TransitMode MapMode(XElement? mode, XElement? service = null)
	{
		string? pt =
			mode.Deep("PtMode").Select(element => element.Text()).FirstOrDefault(text => text is not null)
			?? service.Deep("PtMode").Select(element => element.Text()).FirstOrDefault(text => text is not null);

		string submode =
			string.Concat(
				mode?.Elements()
					.Where(child => child.Name.LocalName.EndsWith("Submode", StringComparison.Ordinal))
					.Select(child => child.Value.Trim()) ?? []);

		TransitMode result = FromPtMode(pt, submode);

		if (result != TransitMode.Unknown)
		{
			return result;
		}

		// No usable PtMode: the name of the submode element ("TramSubmode", "MetroSubmode", ...) says it too.
		foreach (XElement child in mode?.Elements() ?? [])
		{
			string name = child.Name.LocalName;

			if (name.EndsWith("Submode", StringComparison.Ordinal))
			{
				result = FromPtMode(name[..^"Submode".Length].ToLowerInvariant() switch
				{
					"bus" => "bus",
					"trolleybus" => "trolleyBus",
					"coach" => "coach",
					"tram" => "tram",
					"metro" => "metro",
					"rail" => "rail",
					"urbanrail" => "urbanRail",
					"water" => "water",
					"telecabin" or "cableway" => "cableway",
					"funicular" => "funicular",
					"taxi" => "taxi",
					"air" => "air",
					_ => null
				}, child.Value.Trim());

				if (result != TransitMode.Unknown)
				{
					return result;
				}
			}
		}

		// Free texts: "Straßenbahn", "Bus", "S-Bahn", "Regionalzug" ...
		foreach (string? text in new[]
		{
			mode.ChildLabel("Name"),
			service.Child("ProductCategory").ChildLabel("Name"),
			service.Child("ProductCategory").ChildText("ShortName"),
			service.ChildLabel("PublishedServiceName") ?? service.ChildLabel("PublishedLineName")
		})
		{
			result = FromText(text);

			if (result != TransitMode.Unknown)
			{
				return result;
			}
		}

		return TransitMode.Unknown;
	}

	private static TransitMode FromPtMode(string? pt, string submode)
	{
		if (string.IsNullOrWhiteSpace(pt))
		{
			return TransitMode.Unknown;
		}

		return pt.Trim() switch
		{
			var value when value.Equals("bus", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("trolleyBus", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("coach", StringComparison.OrdinalIgnoreCase) => TransitMode.Bus,
			var value when value.Equals("tram", StringComparison.OrdinalIgnoreCase) => TransitMode.Tram,
			var value when value.Equals("metro", StringComparison.OrdinalIgnoreCase) => TransitMode.Subway,
			var value when value.Equals("urbanRail", StringComparison.OrdinalIgnoreCase) => TransitMode.SuburbanRail,
			var value when value.Equals("intercityRail", StringComparison.OrdinalIgnoreCase) => TransitMode.LongDistanceTrain,
			var value when value.Equals("rail", StringComparison.OrdinalIgnoreCase)
				=> submode.Contains("urban", StringComparison.OrdinalIgnoreCase)
					|| submode.Contains("suburban", StringComparison.OrdinalIgnoreCase)
					? TransitMode.SuburbanRail
					: submode.Contains("longDistance", StringComparison.OrdinalIgnoreCase)
						|| submode.Contains("highSpeed", StringComparison.OrdinalIgnoreCase)
						|| submode.Contains("interRegional", StringComparison.OrdinalIgnoreCase)
						|| submode.Contains("international", StringComparison.OrdinalIgnoreCase)
						? TransitMode.LongDistanceTrain
						: TransitMode.RegionalTrain,
			var value when value.Equals("water", StringComparison.OrdinalIgnoreCase) => TransitMode.Ferry,
			var value when value.Equals("cableway", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("funicular", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("telecabin", StringComparison.OrdinalIgnoreCase)
				|| value.Equals("lift", StringComparison.OrdinalIgnoreCase) => TransitMode.CableCar,
			var value when value.Equals("taxi", StringComparison.OrdinalIgnoreCase) => TransitMode.Taxi,
			_ => TransitMode.Unknown
		};
	}

	/// <summary>Vehicle type from a human-readable text or a line name ("S1", "RE 15", "Straßenbahn").</summary>
	private static TransitMode FromText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return TransitMode.Unknown;
		}

		string value = text.Trim();

		if (value.Contains("straßenbahn", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("strassenbahn", StringComparison.OrdinalIgnoreCase)
			|| value.Equals("tram", StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.Tram;
		}

		if (value.Contains("bus", StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.Bus;
		}

		if (value.Contains("s-bahn", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("stadtbahn", StringComparison.OrdinalIgnoreCase)
			|| SuburbanLine.IsMatch(value))
		{
			return TransitMode.SuburbanRail;
		}

		if (value.Contains("u-bahn", StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.Subway;
		}

		if (value.Contains("fähre", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("faehre", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("schiff", StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.Ferry;
		}

		if (value.Contains("schwebebahn", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("seilbahn", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("standseilbahn", StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.CableCar;
		}

		if (LongDistanceLine.IsMatch(value))
		{
			return TransitMode.LongDistanceTrain;
		}

		if (RegionalLine.IsMatch(value)
			|| value.Contains("regional", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("zug", StringComparison.OrdinalIgnoreCase))
		{
			return TransitMode.RegionalTrain;
		}

		return TransitMode.Unknown;
	}

	private static readonly System.Text.RegularExpressions.Regex SuburbanLine =
		new(@"^S\s?\d{1,2}\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

	private static readonly System.Text.RegularExpressions.Regex LongDistanceLine =
		new(@"^(ICE|IC|EC|ECE|RJ|RJX|NJ|FLX|TGV)\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

	private static readonly System.Text.RegularExpressions.Regex RegionalLine =
		new(@"^(RE|RB|IRE|MRB|OE|U\d|VIA|TLX|ODEG|SB)\s?\d*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

	private static PlatformKind KindFor(TransitMode mode) =>
		mode is TransitMode.RegionalTrain or TransitMode.LongDistanceTrain or TransitMode.SuburbanRail
			? PlatformKind.Railtrack
			: PlatformKind.Platform;

	/// <summary>
	/// The visible line name. TRIAS 1.2 calls it <c>PublishedServiceName</c> (1.1: <c>PublishedLineName</c>); the
	/// product category and the mode text are the fallbacks, never the word "Unknown".
	/// </summary>
	/// <summary>
	/// TRIAS 1.4 puts the line properties (LineRef, DirectionRef, Mode, PublishedLineName, OperatorRef) into the
	/// <c>ServiceSection</c>s of a service; a service can have several (the line name changes along the journey).
	/// Older versions put them directly under the service. The first section describes where the leg starts.
	/// </summary>
	private static XElement? Properties(XElement? service) =>
		service.Child("ServiceSection") ?? service;

	private static string LineName(XElement? owner) =>
		LineNameOf(Properties(owner));

	private static string LineNameOf(XElement? service) =>
		service.ChildLabel("PublishedServiceName")
		?? service.ChildLabel("PublishedLineName")
		?? service.Child("ProductCategory").ChildText("ShortName")
		?? service.Child("ProductCategory").ChildLabel("Name")
		?? ShortRef(service.ChildText("LineRef"))
		?? service.Child("Mode").ChildLabel("Name")
		?? "?";

	/// <summary>"ddb:11011: :H" is not a name; the last meaningful token is the best guess.</summary>
	private static string? ShortRef(string? reference)
	{
		if (string.IsNullOrWhiteSpace(reference))
		{
			return null;
		}

		string[] parts = reference.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		return parts.Length > 0
			? parts.Length > 1 ? parts[1] : parts[0]
			: null;
	}

	private static TransitLine MapService(XElement? service, TransitMode mode)
	{
		XElement? section = Properties(service);

		return new()
		{
			Name = LineName(service),
			Mode = mode,
			Operator = section.ChildText("OperatorRef"),
			Destination = service.ChildLabel("DestinationText"),
			DirectionId = section.ChildText("DirectionRef")
		};
	}

	/// <summary>TRIAS 1.4 <c>OccupancyEnumeration</c>: low, moderate, high.</summary>
	private static OccupancyLevel MapOccupancy(string? value) =>
		value?.Trim().ToLowerInvariant() switch
		{
			"low" => OccupancyLevel.Low,
			"moderate" => OccupancyLevel.Medium,
			"high" => OccupancyLevel.High,
			_ => OccupancyLevel.Unknown
		};

	/// <summary>The vehicle of a service: its operator and, when the server says so, how full it is.</summary>
	private static Vehicle? MapVehicle(XElement? service, TransitLine line)
	{
		OccupancyLevel occupancy = MapOccupancy(service.ChildText("Occupancy"));
		string? vehicleRef = service.ChildText("VehicleRef");

		return occupancy == OccupancyLevel.Unknown && vehicleRef is null
			? null
			: new Vehicle
			{
				Id = vehicleRef,
				Name = line.Name,
				Operator = line.Operator,
				Occupancy = occupancy
			};
	}

	private static bool IsCancelled(XElement? service) =>
		service.Child("Cancelled").Flag();

	private static IReadOnlyList<string> Attributes(XElement? service, string lineName) =>
		[.. service
			.Children("Attribute")
			.Select(attribute => attribute.ChildLabel("Text") ?? attribute.Label())
			.Where(text => !string.IsNullOrWhiteSpace(text) && text != lineName)
			.Cast<string>()
			.Distinct()];

	// ---------- Times ----------

	private static (DateTimeOffset? Planned, DateTimeOffset? Estimated) Times(XElement? call, string name)
	{
		XElement? element = call.Child(name);

		return (element.Child("TimetabledTime").Time(), element.Child("EstimatedTime").Time());
	}

	private static DepartureState StateOf(DateTimeOffset? planned, DateTimeOffset? estimated, bool cancelled)
	{
		if (cancelled)
		{
			return DepartureState.Cancelled;
		}

		if (estimated is not { } real)
		{
			return DepartureState.Unknown;
		}

		return planned is { } plan && real - plan >= TimeSpan.FromMinutes(1)
			? DepartureState.Delayed
			: DepartureState.InTime;
	}

	private static (string? Name, PlatformKind Kind, string? Platform) Bay(XElement? call, TransitMode mode)
	{
		string? bay = call.ChildLabel("EstimatedBay") ?? call.ChildLabel("PlannedBay");

		return (bay, KindFor(mode), bay);
	}

	private static StopTime MapStopTime(XElement call, TransitMode mode)
	{
		(DateTimeOffset? plannedArrival, DateTimeOffset? estimatedArrival) = Times(call, "ServiceArrival");
		(DateTimeOffset? plannedDeparture, DateTimeOffset? estimatedDeparture) = Times(call, "ServiceDeparture");
		string? platform = call.ChildLabel("EstimatedBay") ?? call.ChildLabel("PlannedBay");
		PlatformKind kind = platform is null ? PlatformKind.Unknown : KindFor(mode);
		bool skipped = call.Child("NotServicedStop").Flag();

		return new StopTime
		{
			Station = MapStop(call, "StopPointRef", "StopPointName", platform, kind),
			ScheduledArrival = plannedArrival,
			RealtimeArrival = estimatedArrival,
			ScheduledDeparture = plannedDeparture,
			RealtimeDeparture = estimatedDeparture,
			Platform = platform,
			PlatformKind = kind,
			IsCancelled = skipped
		};
	}

	// ---------- Trips ----------

	public static IReadOnlyList<Journey> MapJourneys(XDocument document, Location from, Location to)
	{
		Station origin = ToStation(from);
		Station destination = ToStation(to);
		IReadOnlyDictionary<string, string> situations = SituationTexts(document);

		return
			[.. document
				.Deep("TripResult")
				.Select(result => MapJourney(result, origin, destination, situations))
				.OfType<Journey>()];
	}

	// ---------- Situations ----------

	/// <summary>
	/// The situation messages of a response by situation number. They sit once in the response context
	/// (<c>Situations/PtSituation</c>, SIRI SX) and are referenced from legs and stops by <c>SituationFullRef</c>.
	/// </summary>
	private static IReadOnlyDictionary<string, string> SituationTexts(XDocument document)
	{
		var texts = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (XElement situation in document.Deep("PtSituation"))
		{
			string? number = situation.ChildText("SituationNumber");

			string? text =
				situation.ChildLabel("Summary")
				?? situation.ChildLabel("Description")
				?? situation.ChildLabel("Detail");

			if (number is not null && text is not null)
			{
				texts[number] = text;
			}
		}

		return texts;
	}

	/// <summary>The messages a scope refers to, each once.</summary>
	private static IReadOnlyList<string> SituationsOf(
		IEnumerable<XElement> references,
		IReadOnlyDictionary<string, string> situations) =>
		[.. references
			.Select(reference => reference.ChildText("SituationNumber"))
			.Where(number => number is not null && situations.ContainsKey(number))
			.Select(number => situations[number!])
			.Distinct()];

	private static Station ToStation(Location place) =>
		new()
		{
			Id = place.Id ?? string.Empty,
			ProviderId = place.ProviderId,
			Name = place.Name,
			Place = place.Place,
			Latitude = place.Latitude,
			Longitude = place.Longitude
		};

	private static Journey? MapJourney(
		XElement result,
		Station origin,
		Station destination,
		IReadOnlyDictionary<string, string> situations)
	{
		XElement? trip = result.Child("Trip");

		if (trip is null)
		{
			return null;
		}

		var legs = new List<JourneyLeg>();
		var transfers = new List<JourneyTransfer>();

		// Walks before the next ride are held back until that ride exists (and its index is known).
		var pending = new List<XElement>();

		foreach (XElement tripLeg in trip.Children("TripLeg"))
		{
			if (tripLeg.Child("TimedLeg") is { } timed)
			{
				JourneyLeg leg = MapTimedLeg(tripLeg, timed, situations);
				int index = legs.Count;

				legs.Add(leg);

				foreach (XElement walk in pending)
				{
					transfers.Add(MapTransfer(walk, legs, index - 1 >= 0 ? index - 1 : null, index));
				}

				pending.Clear();
			}
			else
			{
				pending.Add(tripLeg);
			}
		}

		if (legs.Count == 0)
		{
			return null;
		}

		foreach (XElement walk in pending)
		{
			transfers.Add(MapTransfer(walk, legs, legs.Count - 1, null));
		}

		AddWaiting(legs, transfers);

		string? id = trip.ChildText("TripId") ?? result.ChildText("ResultId");

		return new Journey
		{
			From = legs[0].From,
			To = legs[^1].To,
			Origin = origin,
			Destination = destination,
			Legs = legs,
			Transfers = transfers,
			Id = id,
			ProviderId = TriasProviderInfo.Id,
			ProviderData = result,
			PlannedDuration = trip.Child("Duration").Duration(),
			Fares = MapFares(result),
			Notices = SituationsOf(trip.Children("SituationFullRef"), situations)
		};
	}

	private static JourneyLeg MapTimedLeg(
		XElement tripLeg,
		XElement timed,
		IReadOnlyDictionary<string, string> situations)
	{
		XElement? service = timed.Child("Service");
		TransitMode mode = MapMode(Properties(service).Child("Mode"), Properties(service));
		XElement? board = timed.Child("LegBoard");
		XElement? alight = timed.Child("LegAlight");

		var calls = new List<XElement>();

		if (board is not null)
		{
			calls.Add(board);
		}

		calls.AddRange(timed.Children("LegIntermediates"));

		if (alight is not null)
		{
			calls.Add(alight);
		}

		(DateTimeOffset? plannedDeparture, DateTimeOffset? estimatedDeparture) = Times(board, "ServiceDeparture");
		(DateTimeOffset? plannedArrival, DateTimeOffset? estimatedArrival) = Times(alight, "ServiceArrival");

		(string? fromBay, PlatformKind fromKind, _) = Bay(board, mode);
		(string? toBay, PlatformKind toKind, _) = Bay(alight, mode);

		TransitLine line = MapService(service, mode);

		return new JourneyLeg
		{
			Mode = mode,
			From = MapStop(board, "StopPointRef", "StopPointName", fromBay, fromBay is null ? PlatformKind.Unknown : fromKind),
			To = MapStop(alight, "StopPointRef", "StopPointName", toBay, toBay is null ? PlatformKind.Unknown : toKind),
			Stops = [.. calls.Select(call => MapStopTime(call, mode))],
			Path = MapPath(timed.Child("LegTrack")),
			Line = line,
			ScheduledDeparture = plannedDeparture,
			RealtimeDeparture = estimatedDeparture,
			ScheduledArrival = plannedArrival,
			RealtimeArrival = estimatedArrival,
			DeparturePlatform = fromBay,
			DeparturePlatformKind = fromBay is null ? PlatformKind.Unknown : fromKind,
			ArrivalPlatform = toBay,
			ArrivalPlatformKind = toBay is null ? PlatformKind.Unknown : toKind,
			IsCancelled = IsCancelled(service),
			Vehicle = MapVehicle(service, line),
			Notices =
				[.. Attributes(service, line.Name)
					.Concat(SituationsOf(timed.Deep("SituationFullRef"), situations))
					.Distinct()],
			Id = tripLeg.ChildText("LegId"),
			ProviderData = tripLeg
		};
	}

	private static IReadOnlyList<(double Latitude, double Longitude)> MapPath(XElement? track) =>
		track is null
			? []
			: [.. track
				.Deep("Position")
				.Select(position => (Latitude: position.Child("Latitude").Number(), Longitude: position.Child("Longitude").Number()))
				.Where(point => point.Latitude is not null && point.Longitude is not null)
				.Select(point => (point.Latitude!.Value, point.Longitude!.Value))];

	private static JourneyTransfer MapTransfer(XElement tripLeg, List<JourneyLeg> legs, int? previous, int? next)
	{
		XElement? walk =
			tripLeg.Child("ContinuousLeg")
			?? tripLeg.Child("InterchangeLeg")
			?? tripLeg.Child("TransferLeg");

		Station? location =
			(previous is { } before ? legs[before].To : null)
			?? (next is { } after ? legs[after].From : null);

		Station Point(string name) =>
			new()
			{
				Id = StationId(
					walk.Child(name).Child("LocationRef").ChildText("StopPointRef")
					?? walk.Child(name).Child("LocationRef").ChildText("StopPlaceRef")),
				ProviderId = TriasProviderInfo.Id,
				Name = walk.Child(name).Child("LocationRef").ChildLabel("LocationName")
					?? location?.Name
					?? string.Empty,
				Latitude = walk.Child(name).Child("LocationRef").Child("GeoPosition").Child("Latitude").Number(),
				Longitude = walk.Child(name).Child("LocationRef").Child("GeoPosition").Child("Longitude").Number()
			};

		// TRIAS 1.4: InterchangeMode is walk | protectedConnection | guaranteedConnection | remainInVehicle | ...
		// (ContinuousMode for a continuous leg). Only the last three are promises of the operator.
		string mode = walk.ChildText("InterchangeMode") ?? walk.ChildText("ContinuousMode") ?? string.Empty;
		bool remain = mode.Equals("remainInVehicle", StringComparison.OrdinalIgnoreCase);
		bool guaranteed =
			remain
			|| mode.Equals("protectedConnection", StringComparison.OrdinalIgnoreCase)
			|| mode.Equals("guaranteedConnection", StringComparison.OrdinalIgnoreCase);

		// Duration is the whole change; WalkDuration the walking part of it, BufferTime the reserve.
		TimeSpan duration = walk.Child("WalkDuration").Duration() ?? walk.Child("Duration").Duration() ?? TimeSpan.Zero;

		Station start = Point("LegStart");
		Station end = Point("LegEnd");
		bool sameStation = string.Equals(start.Id, end.Id, StringComparison.OrdinalIgnoreCase);

		TransferKind kind =
			remain
				? TransferKind.SameStop
				: guaranteed
					? TransferKind.Waiting
					: !sameStation
						? TransferKind.Walk
						: duration > TimeSpan.Zero || tripLeg.Child("InterchangeLeg") is not null
							? TransferKind.PlatformChange
							: TransferKind.SameStop;

		return new JourneyTransfer
		{
			Location = location ?? start,
			PreviousLegIndex = previous,
			NextLegIndex = next,
			Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration,
			Kind = kind,
			Path = MapPath(walk),
			IsGuaranteed = guaranteed,
			IsEnsured = guaranteed,
			From = start,
			To = end,
			ProviderData = tripLeg
		};
	}

	/// <summary>Waiting time of a change: the gap between arrival and next departure minus the walk.</summary>
	private static void AddWaiting(List<JourneyLeg> legs, List<JourneyTransfer> transfers)
	{
		for (int i = 0; i < transfers.Count; i++)
		{
			JourneyTransfer transfer = transfers[i];

			if (transfer.PreviousLegIndex is not { } before
				|| transfer.NextLegIndex is not { } after
				|| legs[before].EffectiveArrival is not { } arrival
				|| legs[after].EffectiveDeparture is not { } departure)
			{
				continue;
			}

			TimeSpan wait = departure - arrival - transfer.Duration;

			transfers[i] = new JourneyTransfer
			{
				Location = transfer.Location,
				PreviousLegIndex = transfer.PreviousLegIndex,
				NextLegIndex = transfer.NextLegIndex,
				Duration = transfer.Duration,
				WaitingTime = wait < TimeSpan.Zero ? TimeSpan.Zero : wait,
				Kind = transfer.Kind,
				Path = transfer.Path,
				IsGuaranteed = transfer.IsGuaranteed,
				IsEnsured = transfer.IsEnsured,
				From = transfer.From,
				To = transfer.To,
				ProviderData = transfer.ProviderData
			};
		}
	}

	// ---------- Fares ----------

	/// <summary>A ticket valid for a day (duration P1D or the word "Tages"/"day") is a day ticket.</summary>
	private static FareKind KindOf(string name, string? duration) =>
		duration is "P1D" or "PT24H"
		|| name.Contains("tages", StringComparison.OrdinalIgnoreCase)
		|| name.Contains("day", StringComparison.OrdinalIgnoreCase)
			? FareKind.Day
			: name.Contains("einzel", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("single", StringComparison.OrdinalIgnoreCase)
					? FareKind.Single
					: FareKind.Other;

	/// <summary>
	/// TRIAS 1.4 quotes tickets in <c>TripFares/Ticket</c> (TicketName, Price, Currency, TariffLevel, ValidFor,
	/// ValidityDurationText, SaleUrl); the passed fare zones are in <c>PassedZones</c>. Older servers used
	/// <c>FareProduct</c>, which is still read.
	/// </summary>
	private static IReadOnlyList<JourneyFare> MapFares(XElement result)
	{
		var fares = new List<JourneyFare>();

		foreach (XElement tripFares in result.Deep("TripFares"))
		{
			string? zones =
				string.Join(
					", ",
					tripFares.Deep("FareZoneText")
						.Select(zone => zone.Text())
						.OfType<string>()
						.Distinct()) is { Length: > 0 } joined
					? joined
					: null;

			foreach (XElement ticket in tripFares.Children("Ticket"))
			{
				string? name = ticket.ChildText("TicketName") ?? ticket.ChildText("TicketId");

				if (name is null)
				{
					continue;
				}

				string? level = ticket.ChildLabel("TariffLevelLabel") ?? ticket.ChildText("TariffLevel");
				string? validity = ticket.ChildLabel("ValidityDurationText");

				string? validFor =
					string.Join(", ", ticket.Children("ValidFor").Select(item => item.Text()).OfType<string>()) is { Length: > 0 } who
						? who
						: null;

				fares.Add(
					new JourneyFare
					{
						Name = name,
						Kind = KindOf(name, ticket.ChildText("ValidityDuration")),
						Price = ticket.Child("Price").Decimal(),
						Currency = ticket.ChildText("Currency") ?? "EUR",
						Description = string.Join(" · ", new[] { level, validity }.Where(part => !string.IsNullOrWhiteSpace(part))) is { Length: > 0 } text ? text : null,
						Zones = zones,
						ValidFor = validFor,
						Url = ticket.Child("SaleUrl").ChildText("Url") ?? ticket.Child("InfoUrl").ChildText("Url")
					});
			}
		}

		foreach (XElement product in result.Deep("FareProduct"))
		{
			string? name = product.ChildLabel("FareProductName") ?? product.ChildText("FareProductId");

			if (name is null)
			{
				continue;
			}

			fares.Add(
				new JourneyFare
				{
					Name = name,
					Kind = KindOf(name, null),
					Price = product.Child("Price").Decimal(),
					Currency = product.ChildText("Currency") ?? "EUR",
					Description = product.ChildLabel("TariffLevelName")
						?? product.ChildText("TariffLevel")
						?? product.ChildLabel("Description")
				});
		}

		return [.. fares.DistinctBy(fare => (fare.Name, fare.Price, fare.Description))];
	}

	// ---------- Departures ----------

	public static DepartureBoard MapBoard(XDocument document, Location stop, bool arrival)
	{
		var departures = new List<Departure>();
		string? name = null;

		foreach (XElement result in document.Deep("StopEventResult"))
		{
			XElement? stopEvent = result.Child("StopEvent") ?? result;
			XElement? thisCall = stopEvent.Child("ThisCall")?.Child("CallAtStop") ?? stopEvent.Child("ThisCall");

			if (thisCall is null)
			{
				continue;
			}

			name ??= thisCall.ChildLabel("StopPointName");

			XElement? service = stopEvent.Child("Service");
			TransitMode mode = MapMode(Properties(service).Child("Mode"), Properties(service));

			(DateTimeOffset? planned, DateTimeOffset? estimated) =
				Times(thisCall, arrival ? "ServiceArrival" : "ServiceDeparture");

			if (planned is not { } scheduled)
			{
				continue;
			}

			string? bay = thisCall.ChildLabel("EstimatedBay") ?? thisCall.ChildLabel("PlannedBay");
			bool cancelled = IsCancelled(service) || thisCall.Child("NotServicedStop").Flag();

			var runStops = new List<RunStop>();

			foreach (XElement call in stopEvent.Children("PreviousCall"))
			{
				runStops.Add(MapRunStop(call.Child("CallAtStop") ?? call, RunPosition.Previous, mode));
			}

			runStops.Add(MapRunStop(thisCall, RunPosition.Current, mode));

			foreach (XElement call in stopEvent.Children("OnwardCall"))
			{
				runStops.Add(MapRunStop(call.Child("CallAtStop") ?? call, RunPosition.Onward, mode));
			}

			departures.Add(
				new Departure
				{
					Id = service.ChildText("JourneyRef")
						?? stopEvent.ChildText("StopEventId")
						?? result.ChildText("ResultId")
						?? scheduled.ToString("O", CultureInfo.InvariantCulture),
					StopId = thisCall.ChildText("StopPointRef") ?? stop.Id ?? string.Empty,
					Line = MapService(service, mode),
					Scheduled = scheduled,
					IsArrival = arrival,
					Realtime = estimated,
					Platform = bay,
					PlatformKind = bay is null ? PlatformKind.Unknown : KindFor(mode),
					State = StateOf(planned, estimated, cancelled),
					ProviderData =
						new TriasRunData(
							runStops,
							service.ChildText("JourneyRef"),
							service.ChildText("OperatingDayRef"))
				});
		}

		return new DepartureBoard
		{
			StopName = name ?? stop.Name,
			StopPlace = stop.Place,
			Departures =
				[.. departures
					.OrderBy(departure => departure.Effective)]
		};
	}

	private static RunStop MapRunStop(XElement call, RunPosition position, TransitMode mode)
	{
		(DateTimeOffset? plannedArrival, DateTimeOffset? estimatedArrival) = Times(call, "ServiceArrival");
		(DateTimeOffset? plannedDeparture, DateTimeOffset? estimatedDeparture) = Times(call, "ServiceDeparture");

		DateTimeOffset? planned = plannedDeparture ?? plannedArrival;
		DateTimeOffset? estimated = estimatedDeparture ?? estimatedArrival;
		string? bay = call.ChildLabel("EstimatedBay") ?? call.ChildLabel("PlannedBay");

		bool cancelled = call.Child("NotServicedStop").Flag();

		return new RunStop
		{
			Station = MapStop(call, "StopPointRef", "StopPointName", bay, bay is null ? PlatformKind.Unknown : KindFor(mode)),
			Position = position,
			Scheduled = planned,
			Realtime = estimated,
			State = StateOf(planned, estimated, cancelled),
			Occupancy = MapOccupancy(call.ChildText("Occupancy"))
		};
	}

	// ---------- Trip info ----------

	/// <summary>
	/// The calls of one run from a TripInfo answer: <c>PreviousCall</c>, the vehicle position,
	/// <c>OnwardCall</c>. The call at <paramref name="currentStopId"/> is marked as the current one.
	/// The vehicle position, when the server sent one, is written to the log.
	/// </summary>
	public static IReadOnlyList<RunStop> MapRun(XDocument document, string currentStopId)
	{
		XElement? result = document.Deep("TripInfoResult").FirstOrDefault();

		if (result is null)
		{
			return [];
		}

		XElement? service = result.Child("Service");
		TransitMode mode = MapMode(Properties(service).Child("Mode"), Properties(service));

		var stops = new List<RunStop>();

		foreach (XElement element in result.Elements())
		{
			RunPosition? position =
				element.Name.LocalName switch
				{
					"PreviousCall" => RunPosition.Previous,
					"OnwardCall" => RunPosition.Onward,
					_ => null
				};

			if (position is not { } kind)
			{
				continue;
			}

			RunStop stop = MapRunStop(element.Child("CallAtStop") ?? element, kind, mode);

			stops.Add(
				string.Equals(stop.Station.Id, currentStopId, StringComparison.Ordinal)
					? MapRunStop(element.Child("CallAtStop") ?? element, RunPosition.Current, mode)
					: stop);
		}

		if (result.Child("CurrentPosition") is { } vehicle
			&& vehicle.Child("GeoPosition") is { } point)
		{
			DiagnosticLog.Write(
				$"[TRIAS] vehicle at {point.ChildText("Latitude")}, {point.ChildText("Longitude")}"
				+ $" progress {vehicle.ChildText("Progress")}");
		}

		return stops;
	}

	// ---------- Errors ----------

	/// <summary>The first error the service reported, or null.</summary>
	public static (string? Code, string? Text)? Error(XDocument document)
	{
		XElement? error = document.Deep("ErrorMessage").FirstOrDefault();

		if (error is null)
		{
			return null;
		}

		return (error.ChildText("Code"), error.ChildLabel("Text") ?? error.Label());
	}

	public static bool IsNoResult(string? code) =>
		code is { } value
		&& (value.Contains("NOTRIP", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("NO_TRIP", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("NORESULT", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("NO_RESULT", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("NOTFOUND", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase));
}
