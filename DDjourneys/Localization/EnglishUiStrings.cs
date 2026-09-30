namespace DDjourneys.Localization;

public sealed class EnglishUiStrings : IUiStrings
{
	public CommonStrings Common { get; } = new()
	{
		Ok = "OK",
		Cancel = "Cancel",
		Retry = "Try again",
		Close = "Close",
		Loading = "Loading",
		Searching = "Searching",
		Today = "Today",
		Tomorrow = "Tomorrow",
		Unknown = "Unknown",
		SearchAgain = "Search again",
		TryAgain = "Try again",
		Share = "Share",
		Search = "Search",
		Clear = "Clear",
		SearchFailed = "The search failed.",
		CouldNotReachService = "Could not reach the timetable service.",
		SomethingWentWrong = "Something went wrong.",
		NavigationError = "Navigation error.",
		CouldNotOpenJourney = "Could not open journey."
	};

	public PlanStrings Plan { get; } = new()
	{
		Title = "Where to?",
		FromPlaceholder = "From",
		ToPlaceholder = "To",
		ChooseStart = "Choose start",
		ChooseDestination = "Choose destination",
		ToggleFavouriteFrom = "Toggle favourite for start",
		ToggleFavouriteTo = "Toggle favourite for destination",
		SwapDescription = "Swap start and destination",
		Swap = "⇅",
		SearchJourneys = "Search journeys",
		Departure = "Leave",
		Arrival = "Arrive by",
		Date = "Date",
		Time = "Time",
		LeaveNow = "Now",
		Favourites = "Favourites",
		Recent = "Recent",
		StartAndDestinationRequired = "Start and destination are required.",
		PlacesYouSearchForWillAppearHere =
			"Places you search for will appear here."
	};

	public PlaceSearchStrings PlaceSearch { get; } = new()
	{
		Title = "Choose place",
		SearchPlaceholder = "Station, stop or address",
		Hint = "Type a station, stop or address.",
		TypeAtLeastTwoCharacters = "Type at least 2 characters.",
		NoPlacesFound = "No places found.",
		Searching = "Searching...",
		CouldNotReachService =
			"Could not reach the timetable service. Check your connection.",
		SearchForPlace = "Search for a place"
	};

	public ResultsStrings Results { get; } = new()
	{
		Title = "Journeys",
		Loading = "Loading journeys...",
		NoConnections = "No journeys found for this time.",
		JourneysCouldNotBeDisplayed =
			"The journeys could not be displayed.",
		SearchServiceUnavailable =
			"Could not reach the timetable service.",
		Refresh = "Refresh",
		Retry = "Try again"
	};

	public JourneyStrings Journey { get; } = new()
	{
		Title = "Journey",
		Refresh = "Refresh",
		Notes = "Notes",
		NoRealtimeData = "No realtime data",
		ShareJourney = "Share journey",
		NotServed = "Not served",
		InProgress = "In progress",
		Cancelled = "Cancelled",
		CouldNotBeDisplayed =
			"This journey could not be displayed.",
		ShowOrHideIntermediateStops =
			"Show or hide intermediate stops",
		Walk = "Walk",
		ShareTitle = "Share journey",

		Direct = "Direct",
		OneTransfer = "1 transfer",
		MultipleTransfers = "{0} transfers",
		OneNotice = "1 notice",
		MultipleNotices = "{0} notices",
		AccessibilitySummary =
			"Departs {0}, arrives {1}, {2}, {3}",
		AccessibilityCancelled = ", cancelled",
		AccessibilityArrivalDelay = ", arrival {0}",
		AccessibilityNotices = ", {0}",

		HideStops = "Hide stops",
		OneStop = "1 stop",
		MultipleStops = "{0} stops",
		To = "to",
		ContinueFrom = "continue from",
		ImmediateChange = "Immediate change",
		ToChange = "to change",
		WalkAbout = "Walk about",
		BetweenStops = "between stops",
		ConnectionMayBeMissed = "Connection may be missed",
		ChangeAt = "Change at",
		Platform = "Platform",
		LowFloor = "Low floor",
		WheelchairAccessible = "Wheelchair accessible",
		BicycleAccessible = "Bicycle accessible",
		Depart = "depart",
		Arrive = "arrive",
		ShowMore = "Show more",
		ShowLess = "Show less",
		ShowFullNotice = "Show the full notice",
		ShowLessNotice = "Show less of the notice",
		Warning = "Warning",
		Notice = "Notice",
		OnTime = "On time"
	};

	public SettingsStrings Settings { get; } = new()
	{
		Title = "Settings",
		Appearance = "App",
		Theme = "Theme",
		Language = "Language",
		JourneyOptions = "Journeys",
		About = "About",
		ThemeSystem = "System",
		ThemeSystemDescription =
			"Follows the device; dark becomes AMOLED",
		ThemeLight = "Light",
		ThemeLightDescription = "Bright surfaces",
		ThemeDark = "Dark",
		ThemeDarkDescription = "Solarized-style dark",
		ThemeAmoled = "Dark AMOLED",
		ThemeAmoledDescription =
			"Pure black, strong outlines",
		Animations = "Animations",
		AnimationsDescription =
			"Subtle transitions and effects",
		TechnicalDetails = "Technical details",
		TechnicalDetailsDescription =
			"Show extra detail in notices",
		Results = "Results",
		ResultsDescription = "Journeys per search",
		RequestTimeout = "Request timeout",
		RequestTimeoutDescription = "Give up after {0} s",
		ArriveBy = "Arrive by",
		ArriveByDescription =
			"Open the planner in arrival mode",
		WalkingLegs = "Walking legs",
		WalkingLegsDescription =
			"Show walks in the timeline",
		ExpandNotices = "Expand notices",
		ExpandNoticesDescription =
			"Open journey notices by default",
		ResetJourneySettings = "Reset journey settings",
		VersionPrefix = "DDjourneys {0} ({1})"
	};

	public TransportStrings Transport { get; } = new()
	{
		Walk = "Walk",
		Bus = "Bus",
		Tram = "Tram",
		Subway = "Subway",
		SuburbanRail = "Suburban rail",
		RegionalTrain = "Regional train",
		LongDistanceTrain = "Long-distance train",
		Ferry = "Ferry",
		CableCar = "Cable car",
		Taxi = "Taxi",
		OnDemand = "On-demand"
	};
}