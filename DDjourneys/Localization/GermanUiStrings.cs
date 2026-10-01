namespace DDjourneys.Localization;

public sealed class GermanUiStrings : IUiStrings
{
	public CommonStrings Common { get; } = new()
	{
		Ok = "OK",
		Cancel = "Abbrechen",
		Retry = "Erneut versuchen",
		Close = "Schlie\u00dfen",
		Loading = "L\u00e4dt",
		Searching = "Suche",
		Today = "Heute",
		Tomorrow = "Morgen",
		Unknown = "Unbekannt",
		SearchAgain = "Erneut suchen",
		TryAgain = "Erneut versuchen",
		Share = "Teilen",
		Search = "Suchen",
		Clear = "Leeren",
		SearchFailed = "Die Suche ist fehlgeschlagen.",
		CouldNotReachService =
			"Der Fahrplandienst ist nicht erreichbar.",
		SomethingWentWrong = "Etwas ist schiefgelaufen.",
		NavigationError = "Navigationsfehler.",
		CouldNotOpenJourney = "Verbindung konnte nicht ge\u00f6ffnet werden."
	};

	public PlanStrings Plan { get; } = new()
	{
		Title = "Wohin?",
		FromPlaceholder = "Von",
		ToPlaceholder = "Nach",
		ChooseStart = "Start w\u00e4hlen",
		ChooseDestination = "Ziel w\u00e4hlen",
		ToggleFavouriteFrom =
			"Start als Favorit umschalten",
		ToggleFavouriteTo =
			"Ziel als Favorit umschalten",
		SwapDescription = "Start und Ziel tauschen",
		Swap = "\u21c5",
		SearchJourneys = "Verbindungen suchen",
		Departure = "Abfahrt",
		Arrival = "Ankunft bis",
		Date = "Datum",
		Time = "Zeit",
		LeaveNow = "Jetzt",
		Favourites = "Favoriten",
		Recent = "Zuletzt",
		StartAndDestinationRequired =
			"Start und Ziel sind erforderlich.",
		PlacesYouSearchForWillAppearHere =
			"Gesuchte Orte erscheinen hier."
	};

	public PlaceSearchStrings PlaceSearch { get; } = new()
	{
		Title = "Ort wählen",
		SearchPlaceholder =
			"Bahnhof, Haltestelle oder Adresse",
		Hint =
			"Bahnhof, Haltestelle oder Adresse eingeben.",
		TypeAtLeastTwoCharacters =
			"Mindestens 2 Zeichen eingeben.",
		NoPlacesFound = "Keine Orte gefunden.",
		Searching = "Suche...",
		CouldNotReachService =
			"Der Fahrplandienst ist nicht erreichbar. Verbindung prüfen.",
		SearchForPlace = "Nach einem Ort suchen"
	};

	public ResultsStrings Results { get; } = new()
	{
		Title = "Verbindungen",
		Loading = "Verbindungen werden geladen...",
		NoConnections =
		"Für diese Zeit wurden keine Verbindungen gefunden.",
		JourneysCouldNotBeDisplayed =
		"Die Verbindungen konnten nicht angezeigt werden.",
		SearchServiceUnavailable =
		"Der Fahrplandienst ist nicht erreichbar.",
		Refresh = "Aktualisieren",
		Retry = "Erneut versuchen",
		Previous = "Früher",
		Next = "Später"
	};

	public JourneyStrings Journey { get; } = new()
	{
		Title = "Verbindung",
		FollowJourney = "Verbindung verfolgen",
		DeactivateTracking = "Verfolgung beenden",
		Refresh = "Aktualisieren",
		Notes = "Hinweise",
		NoRealtimeData = "Keine Echtzeitdaten",
		ShareJourney = "Verbindung teilen",
		NotServed = "Wird nicht bedient",
		InProgress = "Unterwegs",
		Cancelled = "Ausgefallen",
		CouldNotBeDisplayed =
			"Diese Verbindung konnte nicht angezeigt werden.",
		ShowOrHideIntermediateStops =
			"Zwischenhalte ein- oder ausblenden",
		Walk = "Zu Fu\u00df",
		ShareTitle = "Verbindung teilen",

		Direct = "Direkt",
		OneTransfer = "1 Umstieg",
		MultipleTransfers = "{0} Umstiege",
		OneNotice = "1 Hinweis",
		MultipleNotices = "{0} Hinweise",
		AccessibilitySummary =
			"Abfahrt {0}, Ankunft {1}, {2}, {3}",
		AccessibilityCancelled = ", ausgefallen",
		AccessibilityArrivalDelay = ", Ankunft {0}",
		AccessibilityNotices = ", {0}",

		HideStops = "Haltestellen ausblenden",
		OneStop = "1 Halt",
		MultipleStops = "{0} Halte",
		To = "nach",
		ContinueFrom = "weiter ab",
		ImmediateChange = "Sofortiger Umstieg",
		ToChange = "zum Umsteigen",
		WalkAbout = "Etwa zu Fu\u00df",
		BetweenStops = "zwischen Haltestellen",
		ConnectionMayBeMissed =
			"Anschluss kann verpasst werden",
		ChangeAt = "Umstieg bei",
		Platform = "Gleis",
		LowFloor = "Niederflur",
		WheelchairAccessible =
			"Rollstuhlgerecht",
		BicycleAccessible =
			"Fahrradmitnahme m\u00f6glich",
		Depart = "Abfahrt",
		Arrive = "Ankunft",
		ShowMore = "Mehr anzeigen",
		ShowLess = "Weniger anzeigen",
		ShowFullNotice = "Den vollst\u00e4ndigen Hinweis anzeigen",
		ShowLessNotice = "Hinweis einklappen",
		Warning = "Warnung",
		Notice = "Hinweis",
		OnTime = "P\u00fcnktlich",

		// Occupancy strings
		OccupancyVeryLow = "Sehr wenige Fahrgäste",
		OccupancyLow = "Wenige Fahrgäste",
		OccupancyMedium = "Mittel",
		OccupancyHigh = "Viele Fahrgäste",
		OccupancyFull = "Voll",
		OccupancyOverloaded = "Überfüllt"
	};

	public SettingsStrings Settings { get; } = new()
	{
		Title = "Einstellungen",
		Appearance = "App",
		Theme = "Design",
		Language = "Sprache",
		JourneyOptions = "Verbindungen",
		About = "Info",
		ThemeSystem = "System",
		ThemeSystemDescription =
			"Folgt dem Ger\u00e4t; Dunkel wird AMOLED",
		ThemeLight = "Hell",
		ThemeLightDescription = "Helle Fl\u00e4chen",
		ThemeDark = "Dunkel",
		ThemeDarkDescription = "Dunkles Solarized",
		ThemeAmoled = "Dunkel AMOLED",
		ThemeAmoledDescription =
			"Reines Schwarz, starke Konturen",
		ThemeOther = "Anderes Design",
		ThemeOtherDescription = "Aus allen Farbdesigns w\u00e4hlen",
		ThemesTitle = "Farbdesigns",
		ThemesLightHeader = "Hell",
		ThemesDarkHeader = "Dunkel",
		Animations = "Animationen",
		AnimationsDescription =
			"Unaufdringliche \u00dcberg\u00e4nge und Effekte",
		TechnicalDetails = "Technische Details",
		TechnicalDetailsDescription =
			"Zus\u00e4tzliche Details in Hinweisen anzeigen",
		Results = "Ergebnisse",
		ResultsDescription = "Verbindungen pro Suche",
		RequestTimeout = "Zeitlimit",
		RequestTimeoutDescription = "Abbruch nach {0} s",
		ArriveBy = "Ankunft bis",
		ArriveByDescription =
			"Planer im Ankunftsmodus \u00f6ffnen",
		WalkingLegs = "Fu\u00dfwege",
		WalkingLegsDescription =
			"Fu\u00dfwege in der Zeitlinie anzeigen",
		ExpandNotices = "Hinweise ausklappen",
		ExpandNoticesDescription =
			"Verbindungshinweise standardm\u00e4\u00dfig \u00f6ffnen",
		ResetJourneySettings =
			"Verbindungseinstellungen zur\u00fccksetzen",
		VersionPrefix = "DDjourneys {0} ({1})"
	};

	public TransportStrings Transport { get; } = new()
	{
		Walk = "Zu Fu\u00df",
		Bus = "Bus",
		Tram = "Stra\u00dfenbahn",
		Subway = "U-Bahn",
		SuburbanRail = "S-Bahn",
		RegionalTrain = "Regionalbahn",
		LongDistanceTrain = "Fernverkehr",
		Ferry = "F\u00e4hre",
		CableCar = "Seilbahn",
		Taxi = "Taxi",
		OnDemand = "On-Demand"
	};
}
