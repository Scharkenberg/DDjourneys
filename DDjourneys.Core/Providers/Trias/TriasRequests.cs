using System.Globalization;
using System.Xml.Linq;
using DDjourneys.Core.Models;

namespace DDjourneys.Core.Providers.Trias;

/// <summary>
/// Builds the TRIAS request documents (LocationInformation, Trip, StopEvent, TripInfo) for one schema version.
/// Parameters follow the element order of the 1.4 schema (VDVde/TRIAS), which is a sequence.
/// </summary>
internal static class TriasRequests
{
	private static readonly XNamespace T = "trias";
	private static readonly XNamespace S = "http://www.siri.org.uk/siri";

	private static XDocument Envelope(XElement payload, TriasDialect dialect) =>
		new(
			new XDeclaration("1.0", "UTF-8", null),
			new XElement(
				T + "Trias",
				new XAttribute("version", dialect.Version),
				new XAttribute(XNamespace.Xmlns + "siri", S.NamespaceName),
				new XElement(
					T + "ServiceRequest",
					new XElement(
						S + "RequestTimestamp",
						DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
					new XElement(S + "RequestorRef", "OpenService"),
					new XElement(T + "RequestPayload", payload))));

	private static string Number(double value) =>
		value.ToString("F6", CultureInfo.InvariantCulture);

	private static string Stamp(DateTimeOffset value) =>
		value.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);

	/// <summary>A DHID has three parts ("de:14612:28": stop place); more is a stop point (a platform).</summary>
	private static bool IsStopPlace(string id) =>
		id.Count(character => character == ':') <= 2;

	private static XElement Position(double latitude, double longitude) =>
		new(
			T + "GeoPosition",
			new XElement(T + "Longitude", Number(longitude)),
			new XElement(T + "Latitude", Number(latitude)));

	/// <summary>The children of a location reference: a stop by id, anything else by position.</summary>
	private static IEnumerable<XElement> LocationParts(Location place)
	{
		if (place.Kind == PlaceKind.Stop
			&& !string.IsNullOrWhiteSpace(place.Id))
		{
			yield return new XElement(
				T + (IsStopPlace(place.Id) ? "StopPlaceRef" : "StopPointRef"),
				place.Id);
		}
		else if (place.Latitude is { } latitude
			&& place.Longitude is { } longitude)
		{
			yield return Position(latitude, longitude);
		}
		else if (!string.IsNullOrWhiteSpace(place.Id))
		{
			yield return new XElement(T + "StopPlaceRef", place.Id);
		}

		yield return new XElement(
			T + "LocationName",
			new XElement(T + "Text", place.Name));
	}

	private static XElement LocationRef(Location place) =>
		new(T + "LocationRef", LocationParts(place));

	private static IEnumerable<XElement> PtModeFilter(ModeFilter modes)
	{
		if (modes == ModeFilter.All || modes == ModeFilter.None)
		{
			yield break;
		}

		var allowed = new List<string>();

		void Add(ModeFilter flag, params string[] names)
		{
			if (modes.HasFlag(flag))
			{
				allowed.AddRange(names);
			}
		}

		Add(ModeFilter.Tram, "tram");
		Add(ModeFilter.CityBus, "bus");
		Add(ModeFilter.IntercityBus, "coach");
		Add(ModeFilter.SuburbanRailway, "urbanRail");
		Add(ModeFilter.Train, "rail", "intercityRail");
		Add(ModeFilter.Cableway, "cableway", "funicular");
		Add(ModeFilter.Ferry, "water");
		Add(ModeFilter.HailedSharedTaxi, "taxi");

		yield return new XElement(
			T + "PtModeFilter",
			new XElement(T + "Exclude", "false"),
			allowed.Select(mode => new XElement(T + "PtMode", mode)));
	}

	public static XDocument LocationByName(
		string name,
		PlaceKinds kinds,
		int limit,
		TriasDialect dialect) =>
		Envelope(
			new XElement(
				T + "LocationInformationRequest",
				new XElement(
					T + "InitialInput",
					new XElement(T + "LocationName", name)),
				Restrictions(kinds, limit, dialect)),
			dialect);

	public static XDocument LocationByPosition(
		double latitude,
		double longitude,
		PlaceKinds kinds,
		int limit,
		TriasDialect dialect) =>
		Envelope(
			new XElement(
				T + "LocationInformationRequest",
				new XElement(
					T + "InitialInput",
					Position(latitude, longitude)),
				Restrictions(kinds, limit, dialect)),
			dialect);

	private static XElement Restrictions(PlaceKinds kinds, int limit, TriasDialect dialect)
	{
		var restrictions = new XElement(T + "Restrictions");

		if (kinds.HasFlag(PlaceKinds.Stops))
		{
			restrictions.Add(new XElement(T + "Type", "stop"));
		}

		if (kinds.HasFlag(PlaceKinds.Addresses))
		{
			restrictions.Add(new XElement(T + "Type", "address"));
		}

		if (kinds.HasFlag(PlaceKinds.Pois))
		{
			restrictions.Add(new XElement(T + "Type", "poi"));
		}

		restrictions.Add(new XElement(T + "NumberOfResults", limit.ToString(CultureInfo.InvariantCulture)));

		if (dialect.HasExtendedContent)
		{
			// Lets the stop results name their vehicle types.
			restrictions.Add(new XElement(T + "IncludePtModes", "true"));
		}

		return restrictions;
	}

	public static XDocument Trip(JourneyQuery query, TriasDialect dialect)
	{
		var origin =
			new XElement(
				T + "Origin",
				LocationRef(query.From));

		var destination =
			new XElement(
				T + "Destination",
				LocationRef(query.To));

		// The time belongs to the end the passenger fixed.
		if (query.SearchMode == JourneySearchMode.Arrival)
		{
			destination.Add(new XElement(T + "DepArrTime", Stamp(query.DateTime)));
		}
		else
		{
			origin.Add(new XElement(T + "DepArrTime", Stamp(query.DateTime)));
		}

		var request =
			new XElement(
				T + "TripRequest",
				origin,
				destination);

		if (query.Via is { } via)
		{
			request.Add(
				new XElement(
					T + "Via",
					new XElement(T + "ViaPoint", LocationParts(via))));
		}

		request.Add(TripParams(query, dialect));

		return Envelope(request, dialect);
	}

	/// <summary>The Params element in schema order: filters, mobility, policy, content.</summary>
	private static XElement TripParams(JourneyQuery query, TriasDialect dialect)
	{
		RoutingPreferences routing = query.Routing;

		var parameters = new XElement(T + "Params");

		// TripDataFilterGroup
		parameters.Add(PtModeFilter(routing.Modes));

		// TripMobilityFilterGroup: NoSingleStep, NoStairs, NoEscalator, NoElevator, NoRamp, LevelEntrance, ..., WalkSpeed
		bool noStairs =
			routing.AvoidStairs
			|| routing.Accessibility != AccessibilityNeed.None;

		bool noEscalator =
			routing.AvoidEscalators
			|| routing.Accessibility == AccessibilityNeed.High;

		bool noSingleStep =
			routing.Entrance != EntranceNeed.Any
			|| routing.Accessibility == AccessibilityNeed.High;

		if (noSingleStep)
		{
			parameters.Add(new XElement(T + "NoSingleStep", "true"));
		}

		if (noStairs)
		{
			parameters.Add(new XElement(T + "NoStairs", "true"));
		}

		if (noEscalator)
		{
			parameters.Add(new XElement(T + "NoEscalator", "true"));
		}

		if (routing.Accessibility == AccessibilityNeed.High)
		{
			parameters.Add(new XElement(T + "NoElevator", "false"));
		}

		if (dialect.HasMobilityAdditions
			&& routing.Entrance == EntranceNeed.NoStep)
		{
			parameters.Add(new XElement(T + "LevelEntrance", "true"));
		}

		parameters.Add(
			new XElement(
				T + "WalkSpeed",
				routing.Pace switch
				{
					WalkingPace.VerySlow => "50",
					WalkingPace.Slow => "75",
					WalkingPace.Fast => "125",
					WalkingPace.VeryFast => "150",
					_ => "100"
				}));

		// TripPolicyGroup: NumberOfResults, ..., InterchangeLimit, AlgorithmType
		parameters.Add(
			new XElement(
				T + "NumberOfResults",
				Math.Clamp(query.MaxResults, 1, 10).ToString(CultureInfo.InvariantCulture)));

		// A positive integer in the schema: "no transfers" cannot be asked for, the provider filters those out.
		if (routing.MaxTransfers is MaxTransfers.Two or MaxTransfers.One)
		{
			parameters.Add(
				new XElement(
					T + "InterchangeLimit",
					routing.MaxTransfers == MaxTransfers.Two ? "2" : "1"));
		}

		string algorithm =
			routing.Optimisation switch
			{
				RouteOptimisation.FewestChanges => "minChanges",
				RouteOptimisation.LeastWalking => "leastWalking",
				RouteOptimisation.LowestFare => "leastCost",
				_ => routing.FewestTransfers
					? "minChanges"
					: "fastest"
			};

		parameters.Add(new XElement(T + "AlgorithmType", algorithm));

		// TripContentFilterGroup
		parameters.Add(new XElement(T + "IncludeTrackSections", "true"));
		parameters.Add(new XElement(T + "IncludeLegProjection", "true"));

		if (dialect.HasExtendedContent)
		{
			parameters.Add(new XElement(T + "IncludeAccessibility", "true"));
			parameters.Add(new XElement(T + "IncludeEstimatedTimes", "true"));
			parameters.Add(new XElement(T + "IncludeSituationInfo", "true"));
		}

		parameters.Add(new XElement(T + "IncludeIntermediateStops", "true"));
		parameters.Add(new XElement(T + "IncludeFares", "true"));

		if (dialect.HasExtendedContent)
		{
			parameters.Add(new XElement(T + "IncludeOperatingDays", "true"));
		}

		// FaresParam is the last element of the sequence. Adult is the server's default and is not sent.
		if (dialect.HasMobilityAdditions
			&& routing.Passenger != PassengerCategory.Adult)
		{
			parameters.Add(
				new XElement(
					T + "FaresParam",
					new XElement(T + "PassengerCategory", routing.Passenger.ToString())));
		}

		return parameters;
	}

	public static XDocument StopEvents(DepartureQuery query, TriasDialect dialect)
	{
		var location =
			new XElement(
				T + "Location",
				LocationRef(query.Stop),
				new XElement(
					T + "DepArrTime",
					Stamp(query.Time ?? DateTimeOffset.Now)));

		var parameters = new XElement(T + "Params");

		parameters.Add(PtModeFilter(query.Modes));
		parameters.Add(new XElement(T + "NumberOfResults", Math.Clamp(query.Limit, 1, 50).ToString(CultureInfo.InvariantCulture)));
		parameters.Add(new XElement(T + "StopEventType", query.IsArrival ? "arrival" : "departure"));
		parameters.Add(new XElement(T + "IncludePreviousCalls", "true"));
		parameters.Add(new XElement(T + "IncludeOnwardCalls", "true"));

		if (dialect.HasExtendedContent)
		{
			parameters.Add(new XElement(T + "IncludeOperatingDays", "true"));
		}

		parameters.Add(new XElement(T + "IncludeRealtimeData", "true"));

		return Envelope(
			new XElement(
				T + "StopEventRequest",
				location,
				parameters),
			dialect);
	}

	/// <summary>
	/// Asks for one vehicle run again: its calls with current estimates, the vehicle position and the situations.
	/// </summary>
	public static XDocument TripInfo(string journeyRef, string operatingDayRef, TriasDialect dialect)
	{
		var request =
			new XElement(
				T + "TripInfoRequest",
				new XElement(T + "JourneyRef", journeyRef),
				new XElement(T + "OperatingDayRef", operatingDayRef));

		var parameters = new XElement(T + "Params");

		parameters.Add(new XElement(T + "IncludeCalls", "true"));
		parameters.Add(new XElement(T + "IncludeEstimatedTimes", "true"));
		parameters.Add(new XElement(T + "IncludePosition", "true"));
		parameters.Add(new XElement(T + "IncludeService", "true"));

		if (dialect.HasExtendedContent)
		{
			parameters.Add(new XElement(T + "IncludeSituationInfo", "true"));
		}

		request.Add(parameters);

		return Envelope(request, dialect);
	}
}
