namespace DDjourneys.Localization;

public sealed class GermanUiStrings : IUiStrings
{
	public CommonStrings Common { get; } = new()
	{
		Ok = "OK",
		Cancel = "Abbrechen",
		Retry = "Erneut versuchen",
		Close = "Schließen",
		Loading = "Lädt",
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
		CouldNotOpenJourney = "Verbindung konnte nicht geöffnet werden."
	};

	public PlanStrings Plan { get; } = new()
	{
		Title = "Wohin?",
		FromPlaceholder = "Von",
		ToPlaceholder = "Nach",
		ChooseStart = "Start wählen",
		ChooseDestination = "Ziel wählen",
		ToggleFavouriteFrom =
			"Start als Favorit umschalten",
		ToggleFavouriteTo =
			"Ziel als Favorit umschalten",
		SwapDescription = "Start und Ziel tauschen",
		Swap = "⇅",
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
		Retry = "Erneut versuchen"
	};

	public JourneyStrings Journey { get; } = new()
	{
		Title = "Verbindung",
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
		Walk = "Zu Fuß",
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
		WalkAbout = "Etwa zu Fuß",
		BetweenStops = "zwischen Haltestellen",
		ConnectionMayBeMissed =
			"Anschluss kann verpasst werden",
		ChangeAt = "Umstieg bei",
		Platform = "Gleis",
		LowFloor = "Niederflur",
		WheelchairAccessible =
			"Rollstuhlgerecht",
		BicycleAccessible =
			"Fahrradmitnahme möglich",
		Depart = "Abfahrt",
		Arrive = "Ankunft",
		ShowMore = "Mehr anzeigen",
		ShowLess = "Weniger anzeigen",
		ShowFullNotice = "Den vollständigen Hinweis anzeigen",
		ShowLessNotice = "Hinweis einklappen",
		Warning = "Warnung",
		Notice = "Hinweis",
		OnTime = "Pünktlich"
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
			"Folgt dem Gerät; Dunkel wird AMOLED",
		ThemeLight = "Hell",
		ThemeLightDescription = "Helle Flächen",
		ThemeDark = "Dunkel",
		ThemeDarkDescription = "Dunkles Solarized",
		ThemeAmoled = "Dunkel AMOLED",
		ThemeAmoledDescription =
			"Reines Schwarz, starke Konturen",
		Animations = "Animationen",
		AnimationsDescription =
			"Unaufdringliche Übergänge und Effekte",
		TechnicalDetails = "Technische Details",
		TechnicalDetailsDescription =
			"Zusätzliche Details in Hinweisen anzeigen",
		Results = "Ergebnisse",
		ResultsDescription = "Verbindungen pro Suche",
		RequestTimeout = "Zeitlimit",
		RequestTimeoutDescription = "Abbruch nach {0} s",
		ArriveBy = "Ankunft bis",
		ArriveByDescription =
			"Planer im Ankunftsmodus öffnen",
		WalkingLegs = "Fußwege",
		WalkingLegsDescription =
			"Fußwege in der Zeitlinie anzeigen",
		ExpandNotices = "Hinweise ausklappen",
		ExpandNoticesDescription =
			"Verbindungshinweise standardmäßig öffnen",
		ResetJourneySettings =
			"Verbindungseinstellungen zurücksetzen",
		VersionPrefix = "DDjourneys {0} ({1})"
	};

	public TransportStrings Transport { get; } = new()
	{
		Walk = "Zu Fuß",
		Bus = "Bus",
		Tram = "Straßenbahn",
		Subway = "U-Bahn",
		SuburbanRail = "S-Bahn",
		RegionalTrain = "Regionalbahn",
		LongDistanceTrain = "Fernverkehr",
		Ferry = "Fähre",
		CableCar = "Seilbahn",
		Taxi = "Taxi",
		OnDemand = "On-Demand"
	};
}