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
		Searching = "Sucht",
		Today = "Heute",
		Tomorrow = "Morgen",
		Unknown = "Unbekannt",
		SearchAgain = "Erneut suchen",
		TryAgain = "Erneut versuchen",
		Share = "Teilen",
		Search = "Suchen",
		SearchFailed = "Die Suche ist fehlgeschlagen.",
		CouldNotReachService = "Der Fahrplandienst ist nicht erreichbar."
	};

	public PlanStrings Plan { get; } = new()
	{
		Title = "Wohin?",
		FromPlaceholder = "Von",
		ToPlaceholder = "Nach",
		ChooseStart = "Start wählen",
		ChooseDestination = "Ziel wählen",
		Swap = "⇅",
		SearchJourneys = "Verbindungen suchen",
		Departure = "Abfahrt",
		Arrival = "Ankunft bis",
		Date = "Datum",
		Time = "Zeit",
		LeaveNow = "Jetzt",
		Favourites = "Favoriten",
		Recent = "Zuletzt",
		PlacesYouSearchForWillAppearHere = "Gesuchte Orte erscheinen hier."
	};

	public PlaceSearchStrings PlaceSearch { get; } = new()
	{
		Title = "Ort wählen",
		SearchPlaceholder = "Bahnhof, Haltestelle oder Adresse",
		Hint = "Bahnhof, Haltestelle oder Adresse eingeben.",
		TypeAtLeastTwoCharacters = "Mindestens 2 Zeichen eingeben.",
		NoPlacesFound = "Keine Orte gefunden.",
		Searching = "Suche...",
		CouldNotReachService = "Der Fahrplandienst ist nicht erreichbar. Verbindung prüfen."
	};

	public ResultsStrings Results { get; } = new()
	{
		Title = "Verbindungen",
		Loading = "Verbindungen werden geladen...",
		NoConnections = "Für diese Zeit wurden keine Verbindungen gefunden.",
		Refresh = "Aktualisieren",
		Retry = "Erneut versuchen",
		ErrorLoadingConnections = "Der Fahrplandienst ist nicht erreichbar."
	};

	public JourneyStrings Journey { get; } = new()
	{
		Title = "Verbindung",
		Refresh = "Aktualisieren",
		Notes = "Hinweise",
		NoRealtimeData = "Keine Echtzeitdaten",
		ShareJourney = "Verbindung teilen",
		NotServed = "Fällt aus",
		InProgress = "Unterwegs",
		Cancelled = "Ausgefallen",
		CouldNotBeDisplayed = "Diese Verbindung konnte nicht angezeigt werden.",
		ShowOrHideIntermediateStops = "Zwischenhalte ein- oder ausblenden",
		Walk = "Zu Fuß",
		ShareTitle = "Verbindung teilen"
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
		ThemeSystemDescription = "Folgt dem Gerät; Dunkel wird AMOLED",
		ThemeLight = "Hell",
		ThemeLightDescription = "Helle Flächen",
		ThemeDark = "Dunkel",
		ThemeDarkDescription = "Dunkles Solarized",
		ThemeAmoled = "Dunkel AMOLED",
		ThemeAmoledDescription = "Reines Schwarz, starke Konturen",
		Animations = "Animationen",
		AnimationsDescription = "Unaufdringliche Übergänge und Effekte",
		TechnicalDetails = "Technische Details",
		TechnicalDetailsDescription = "Mehr Details in Hinweisen anzeigen",
		Results = "Ergebnisse",
		ResultsDescription = "Verbindungen pro Suche",
		RequestTimeout = "Zeitlimit",
		RequestTimeoutDescription = "Abbruch nach {0} s",
		ArriveBy = "Ankunft bis",
		ArriveByDescription = "Planer im Ankunftsmodus öffnen",
		WalkingLegs = "Fußwege",
		WalkingLegsDescription = "Fußwege in der Zeitlinie anzeigen",
		ExpandNotices = "Hinweise ausklappen",
		ExpandNoticesDescription = "Verbindungshinweise standardmäßig öffnen",
		ResetJourneySettings = "Verbindungseinstellungen zurücksetzen",
		VersionPrefix = "DDjourneys {0} ({1})"
	};
}