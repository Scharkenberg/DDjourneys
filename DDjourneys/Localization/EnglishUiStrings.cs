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
		SearchFailed = "The search failed.",
		CouldNotReachService = "Could not reach the timetable service."
	};

	public PlanStrings Plan { get; } = new()
	{
		Title = "Where to?",
		FromPlaceholder = "From",
		ToPlaceholder = "To",
		ChooseStart = "Choose start",
		ChooseDestination = "Choose destination",
		Swap = "⇅",
		SearchJourneys = "Search journeys",
		Departure = "Leave",
		Arrival = "Arrive by",
		Date = "Date",
		Time = "Time",
		LeaveNow = "Now",
		Favourites = "Favourites",
		Recent = "Recent",
		PlacesYouSearchForWillAppearHere = "Places you search for will appear here."
	};

	public PlaceSearchStrings PlaceSearch { get; } = new()
	{
		Title = "Choose place",
		SearchPlaceholder = "Station, stop or address",
		Hint = "Type a station, stop or address.",
		TypeAtLeastTwoCharacters = "Type at least 2 characters.",
		NoPlacesFound = "No places found.",
		Searching = "Searching...",
		CouldNotReachService = "Could not reach the timetable service. Check your connection."
	};

	public ResultsStrings Results { get; } = new()
	{
		Title = "Journeys",
		Loading = "Loading journeys...",
		NoConnections = "No journeys found for this time.",
		Refresh = "Refresh",
		Retry = "Try again",
		ErrorLoadingConnections = "Could not reach the timetable service."
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
		CouldNotBeDisplayed = "This journey could not be displayed.",
		ShowOrHideIntermediateStops = "Show or hide intermediate stops",
		Walk = "Walk",
		ShareTitle = "Share journey"
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
		ThemeSystemDescription = "Follows the device; dark becomes AMOLED",
		ThemeLight = "Light",
		ThemeLightDescription = "Bright surfaces",
		ThemeDark = "Dark",
		ThemeDarkDescription = "Solarized-style dark",
		ThemeAmoled = "Dark AMOLED",
		ThemeAmoledDescription = "Pure black, strong outlines",
		Animations = "Animations",
		AnimationsDescription = "Subtle transitions and effects",
		TechnicalDetails = "Technical details",
		TechnicalDetailsDescription = "Show extra detail in notices",
		Results = "Results",
		ResultsDescription = "Journeys per search",
		RequestTimeout = "Request timeout",
		RequestTimeoutDescription = "Give up after {0} s",
		ArriveBy = "Arrive by",
		ArriveByDescription = "Open the planner in arrival mode",
		WalkingLegs = "Walking legs",
		WalkingLegsDescription = "Show walks in the timeline",
		ExpandNotices = "Expand notices",
		ExpandNoticesDescription = "Open journey notices by default",
		ResetJourneySettings = "Reset journey settings",
		VersionPrefix = "DDjourneys {0} ({1})"
	};
}