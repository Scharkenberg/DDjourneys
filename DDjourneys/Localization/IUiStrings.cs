namespace DDjourneys.Localization;

public interface IUiStrings
{
	CommonStrings Common { get; }
	PlanStrings Plan { get; }
	PlaceSearchStrings PlaceSearch { get; }
	DeparturesStrings Departures { get; }
	DisruptionsStrings Disruptions { get; }
	ExtrasStrings Extras { get; }
	ResultsStrings Results { get; }
	JourneyStrings Journey { get; }
	TrackingStrings Tracking { get; }
	ExpertStrings Expert { get; }
	ProviderStrings Provider { get; }
	RoutingStrings Routing { get; }
	SettingsStrings Settings { get; }
	TransportStrings Transport { get; }
	WidgetStrings Widgets { get; }
}

public sealed class CommonStrings
{
	public required string Ok { get; init; }
	public required string Cancel { get; init; }
	public required string Retry { get; init; }
	public required string Close { get; init; }
	public required string Back { get; init; }
	public required string Loading { get; init; }
	public required string Searching { get; init; }
	public required string Today { get; init; }
	public required string Tomorrow { get; init; }
	public required string Unknown { get; init; }
	public required string SearchAgain { get; init; }
	public required string TryAgain { get; init; }
	public required string Share { get; init; }
	public required string Search { get; init; }
	public required string Clear { get; init; }
	public required string SearchFailed { get; init; }
	public required string CouldNotReachService { get; init; }
	public required string SomethingWentWrong { get; init; }
	public required string NavigationError { get; init; }
	public required string CouldNotOpenJourney { get; init; }
}

public sealed class PlanStrings
{
	public required string Title { get; init; }
	public required string FromPlaceholder { get; init; }
	public required string ToPlaceholder { get; init; }
	public required string ChooseStart { get; init; }
	public required string ChooseDestination { get; init; }
	public required string ToggleFavouriteFrom { get; init; }
	public required string ToggleFavouriteTo { get; init; }
	public required string SwapDescription { get; init; }
	public required string Swap { get; init; }
	public required string SearchJourneys { get; init; }
	public required string Departure { get; init; }
	public required string Arrival { get; init; }
	public required string Date { get; init; }
	public required string Time { get; init; }
	public required string LeaveNow { get; init; }
	public required string Favourites { get; init; }
	public required string Recent { get; init; }
	public required string RecentSearches { get; init; }
	public required string ShowAllSearches { get; init; }
	public required string ShowFewer { get; init; }
	public required string ForgetSearch { get; init; }
	public required string SearchAgain { get; init; }
	public required string PlacesYouSearchForWillAppearHere { get; init; }
	public required string StartAndDestinationRequired { get; init; }
	public required string Via { get; init; }
	public required string AddVia { get; init; }
	public required string ClearVia { get; init; }
	public required string UseLocationFrom { get; init; }
	public required string UseLocationTo { get; init; }
	public required string GoHome { get; init; }
	public required string SetHome { get; init; }
	public required string SaveRoute { get; init; }
	public required string RouteNamePrompt { get; init; }
	public required string SavedRoutes { get; init; }
	public required string ForgetSavedRoute { get; init; }
	public required string RouteSaved { get; init; }
	public required string RouteRemoved { get; init; }
	public required string LocationPermissionDenied { get; init; }
	public required string LocationUnavailable { get; init; }
	public required string NoStopNearby { get; init; }
}

public sealed class DeparturesStrings
{
	public required string Title { get; init; }
	public required string Stop { get; init; }
	public required string ChooseStop { get; init; }
	public required string DeparturesTab { get; init; }
	public required string ArrivalsTab { get; init; }
	public required string Now { get; init; }
	public required string NoDepartures { get; init; }
	public required string Lines { get; init; }
	public required string ShowLines { get; init; }
	public required string HideLines { get; init; }
	public required string Nearby { get; init; }
	public required string UseMyLocation { get; init; }
	public required string TariffZone { get; init; }
	public required string Metres { get; init; }
	public required string Cancelled { get; init; }
	public required string RouteChanges { get; init; }
	public required string Refresh { get; init; }
	public required string Updated { get; init; }
	public required string Hint { get; init; }
	public required string RunTitle { get; init; }
	public required string NoRun { get; init; }
	public required string VehicleHere { get; init; }
	public required string OpenDepartures { get; init; }
}


public sealed class DisruptionsStrings
{
	public required string Title { get; init; }
	public required string OnlyShortTerm { get; init; }
	public required string FilterPlaceholder { get; init; }
	public required string None { get; init; }
	public required string Planned { get; init; }
	public required string ShortTerm { get; init; }
	public required string AffectsRouting { get; init; }
	public required string From { get; init; }
	public required string Until { get; init; }
	public required string Range { get; init; }
	public required string Notices { get; init; }
	public required string ShowAll { get; init; }
	public required string Selected { get; init; }
	public required string Refresh { get; init; }
	public required string Details { get; init; }
}


public sealed class PlaceSearchStrings
{
	public required string Title { get; init; }
	public required string SearchPlaceholder { get; init; }
	public required string SearchForPlace { get; init; }
	public required string Hint { get; init; }
	public required string TypeAtLeastThreeCharacters { get; init; }
	public required string NoPlacesFound { get; init; }
	public required string Searching { get; init; }
	public required string CouldNotReachService { get; init; }
}

public sealed class ResultsStrings
{
	public required string Title { get; init; }
	public required string Loading { get; init; }
	public required string NoConnections { get; init; }
	public required string JourneysCouldNotBeDisplayed { get; init; }
	public required string SearchServiceUnavailable { get; init; }
	public required string PlacesNotUsable { get; init; }
	public required string Refresh { get; init; }
	public required string Retry { get; init; }
	public required string Previous { get; init; }
	public required string Next { get; init; }
}
public sealed class JourneyStrings
{
	public required string LegEarlier { get; init; }
	public required string LegLater { get; init; }
	public required string LegEarlierHint { get; init; }
	public required string LegLaterHint { get; init; }
	public required string LegNone { get; init; }
	public required string LegSearching { get; init; }
	public required string OpenPdf { get; init; }
	public required string AlternativeShown { get; init; }
	public required string Title { get; init; }
	public required string FollowJourney { get; init; }
	public required string DeactivateTracking { get; init; }
	public required string Refresh { get; init; }
	public required string Notes { get; init; }
	public required string NoRealtimeData { get; init; }
	public required string ShareJourney { get; init; }
	public required string NotServed { get; init; }
	public required string InProgress { get; init; }
	public required string Cancelled { get; init; }
	public required string CouldNotBeDisplayed { get; init; }
	public required string ShowOrHideIntermediateStops { get; init; }
	public required string Walk { get; init; }
	public required string ShareTitle { get; init; }
	public required string ShareAsText { get; init; }
	public required string ShareAsImage { get; init; }
	public required string ShareSubtitle { get; init; }
	public required string ShareTextDescription { get; init; }
	public required string ShareImageDescription { get; init; }
	public required string ShareAsCalendar { get; init; }
	public required string CalendarUnavailable { get; init; }
	public required string ShareCalendarDescription { get; init; }
	public required string ShareChooseFormat { get; init; }
	public required string HandOff { get; init; }
	public required string HandOffFailed { get; init; }
	public required string Direct { get; init; }
	public required string OneTransfer { get; init; }
	public required string MultipleTransfers { get; init; }
	public required string OneNotice { get; init; }
	public required string MultipleNotices { get; init; }
	public required string AccessibilitySummary { get; init; }
	public required string AccessibilityCancelled { get; init; }
	public required string AccessibilityArrivalDelay { get; init; }
	public required string AccessibilityNotices { get; init; }
	public required string HideStops { get; init; }
	public required string OneStop { get; init; }
	public required string MultipleStops { get; init; }
	public required string To { get; init; }
	public required string ContinueFrom { get; init; }
	public required string ImmediateChange { get; init; }
	public required string ToChange { get; init; }
	public required string WalkAbout { get; init; }
	public required string BetweenStops { get; init; }
	public required string ConnectionMayBeMissed { get; init; }

	public required string ConnectionGuaranteed { get; init; }
	public required string NotPossible { get; init; }
	public required string BlockRideCancelled { get; init; }
	public required string BlockNotServed { get; init; }
	public required string BlockConnection { get; init; }
	public required string ConnectionUnreachable { get; init; }
	public required string ChangeAt { get; init; }
	public required string Platform { get; init; }
	public required string WithinStop { get; init; }
	public required string Track { get; init; }
	public required string LowFloor { get; init; }
	public required string WheelchairAccessible { get; init; }
	public required string BicycleAccessible { get; init; }
	public required string Depart { get; init; }
	public required string Arrive { get; init; }
	public required string ShowMore { get; init; }
	public required string ShowLess { get; init; }
	public required string ShowFullNotice { get; init; }
	public required string ShowLessNotice { get; init; }
	public required string Warning { get; init; }
	public required string Notice { get; init; }
	public required string OnTime { get; init; }

	// Occupancy strings
	public required string Occupancy { get; init; }
	public required string OccupancyVeryLow { get; init; }
	public required string OccupancyLow { get; init; }
	public required string OccupancyMedium { get; init; }
	public required string OccupancyHigh { get; init; }
	public required string OccupancyFull { get; init; }
	public required string OccupancyOverloaded { get; init; }
}

public sealed class SettingsStrings
{
	public required string Title { get; init; }
	public required string Appearance { get; init; }
	public required string Theme { get; init; }
	public required string Language { get; init; }
	public required string JourneyOptions { get; init; }
	public required string MapSection { get; init; }
	public required string MapKeyTitle { get; init; }
	public required string MapKeyDescription { get; init; }
	public required string MapKeyPlaceholder { get; init; }
	public required string MapKeySave { get; init; }
	public required string MapKeyStatusMissing { get; init; }
	public required string MapKeyStatusInvalid { get; init; }
	public required string MapKeyStatusSet { get; init; }
	public required string MapEngineTitle { get; init; }
	public required string MapEngineDescription { get; init; }
	public required string MapEngineCarto { get; init; }
	public required string MapEngineCartoDescription { get; init; }
	public required string MapEngineLeaflet { get; init; }
	public required string MapEngineLeafletDescription { get; init; }
	public required string About { get; init; }
	public required string ThemeSystem { get; init; }
	public required string ThemeLight { get; init; }
	public required string ThemeDark { get; init; }
	public required string ThemeSun { get; init; }
	public required string AppearanceTitle { get; init; }
	public required string AppearanceHint { get; init; }
	public required string ModeHeader { get; init; }
	public required string ModeSystemDescription { get; init; }
	public required string ModeLightDescription { get; init; }
	public required string ModeDarkDescription { get; init; }
	public required string ModeSunDescription { get; init; }
	public required string PureBlack { get; init; }
	public required string PureBlackDescription { get; init; }
	public required string ColorsHeader { get; init; }
	public required string ColorSystem { get; init; }
	public required string ColorSystemDescription { get; init; }
	public required string ColorSystemSolarized { get; init; }
	public required string ColorSystemSolarizedDescription { get; init; }
	public required string DensityHeader { get; init; }
	public required string DensityCompact { get; init; }
	public required string DensityCompactDescription { get; init; }
	public required string DensityNormal { get; init; }
	public required string DensityNormalDescription { get; init; }
	public required string DensityTouch { get; init; }
	public required string DensityTouchDescription { get; init; }
	public required string MaterialHeader { get; init; }
	public required string MaterialNone { get; init; }
	public required string MaterialNoneDescription { get; init; }
	public required string MaterialMica { get; init; }
	public required string MaterialMicaDescription { get; init; }
	public required string MaterialMicaAlt { get; init; }
	public required string MaterialMicaAltDescription { get; init; }
	public required string MaterialAcrylic { get; init; }
	public required string MaterialAcrylicDescription { get; init; }
	public required string MaterialNote { get; init; }
	public required string SurfacesHeader { get; init; }
	public required string SurfacesBackdrop { get; init; }
	public required string SurfacesBackdropDescription { get; init; }
	public required string SurfacesLayered { get; init; }
	public required string SurfacesLayeredDescription { get; init; }
	public required string SurfacesImmersive { get; init; }
	public required string SurfacesImmersiveDescription { get; init; }
	public required string FontHeader { get; init; }
	public required string FontSystem { get; init; }
	public required string FontSystemDescription { get; init; }
	public required string FontOpenSans { get; init; }
	public required string FontOpenSansDescription { get; init; }
	public required string FontInterTight { get; init; }
	public required string FontInterTightDescription { get; init; }
	public required string Animations { get; init; }
	public required string AnimationsDescription { get; init; }
	public required string TechnicalDetails { get; init; }
	public required string TechnicalDetailsDescription { get; init; }
	public required string Results { get; init; }
	public required string ResultsDescription { get; init; }
	public required string RequestTimeout { get; init; }
	public required string LeadMinutes { get; init; }
	public required string LeadMinutesDescription { get; init; }
	public required string RequestTimeoutDescription { get; init; }
	public required string ArriveBy { get; init; }
	public required string ArriveByDescription { get; init; }
	public required string WalkingLegs { get; init; }
	public required string WalkingLegsDescription { get; init; }
	public required string ExpandNotices { get; init; }
	public required string ExpandNoticesDescription { get; init; }
	public required string ResetJourneySettings { get; init; }
	public required string JourneyDisplay { get; init; }
	public required string Occupancy { get; init; }
	public required string OccupancyDescription { get; init; }
	public required string Platforms { get; init; }
	public required string PlatformsDescription { get; init; }
	public required string ExpandStops { get; init; }
	public required string ExpandStopsDescription { get; init; }
	public required string ExpertView { get; init; }
	public required string ExpertViewDescription { get; init; }
	public required string DeveloperOptions { get; init; }
	public required string DeveloperOptionsDescription { get; init; }
	public required string LogToFile { get; init; }
	public required string LogToFileDescription { get; init; }
	public required string ShareLog { get; init; }
	public required string ClearLog { get; init; }
	public required string PlaceSearch { get; init; }
	public required string SearchDelay { get; init; }
	public required string SearchDelayDescription { get; init; }
	public required string MinQueryLength { get; init; }
	public required string MinQueryLengthDescription { get; init; }
	public required string RoutingEntry { get; init; }
	public required string RoutingEntryDescription { get; init; }
	public required string StartEntry { get; init; }
	public required string StartEntryDescription { get; init; }
	public required string StartTitle { get; init; }
	public required string StartInputMode { get; init; }
	public required string StartInputModeDescription { get; init; }
	public required string StartFromTitle { get; init; }
	public required string StartFromLocation { get; init; }
	public required string StartFromLocationDescription { get; init; }
	public required string StartFromPlace { get; init; }
	public required string StartFromPlaceDescription { get; init; }
	public required string StartPlaceNone { get; init; }
	public required string StartPlaceHint { get; init; }
	public required string VersionPrefix { get; init; }
	public required string AboutEntryDescription { get; init; }
	public required string InterfaceVersions { get; init; }
	public required string InterfaceVersionsDescription { get; init; }
	public required string CopyInterfaces { get; init; }
	public required string InterfacesCopied { get; init; }
}

public sealed class TransportStrings
{
	public required string Walk { get; init; }
	public required string Bus { get; init; }
	public required string Tram { get; init; }
	public required string Subway { get; init; }
	public required string SuburbanRail { get; init; }
	public required string RegionalTrain { get; init; }
	public required string LongDistanceTrain { get; init; }
	public required string Ferry { get; init; }
	public required string CableCar { get; init; }
	public required string Taxi { get; init; }
	public required string OnDemand { get; init; }
}

public sealed class RoutingStrings
{
	public required string Title { get; init; }
	public required string Hint { get; init; }
	public required string SectionModes { get; init; }
	public required string SectionTransfers { get; init; }
	public required string SectionWalking { get; init; }
	public required string SectionAccessibility { get; init; }
	public required string SectionMore { get; init; }
	public required string Reset { get; init; }
	public required string Tram { get; init; }
	public required string CityBus { get; init; }
	public required string IntercityBus { get; init; }
	public required string SuburbanRailway { get; init; }
	public required string Train { get; init; }
	public required string Cableway { get; init; }
	public required string Ferry { get; init; }
	public required string HailedSharedTaxi { get; init; }
	public required string TransfersUnlimited { get; init; }
	public required string TransfersTwo { get; init; }
	public required string TransfersOne { get; init; }
	public required string TransfersNone { get; init; }
	public required string PaceVerySlow { get; init; }
	public required string PaceSlow { get; init; }
	public required string PaceNormal { get; init; }
	public required string PaceFast { get; init; }
	public required string PaceVeryFast { get; init; }
	public required string Footpath { get; init; }
	public required string FootpathDescription { get; init; }
	public required string AlternativeStops { get; init; }
	public required string AlternativeStopsDescription { get; init; }
	public required string AccessNone { get; init; }
	public required string AccessMedium { get; init; }
	public required string AccessHigh { get; init; }
	public required string AvoidStairs { get; init; }
	public required string AvoidStairsDescription { get; init; }
	public required string AvoidEscalators { get; init; }
	public required string AvoidEscalatorsDescription { get; init; }
	public required string FewestTransfers { get; init; }
	public required string FewestTransfersDescription { get; init; }
	public required string SectionEntrance { get; init; }
	public required string SectionTickets { get; init; }
	public required string EntranceAny { get; init; }
	public required string EntranceSmallStep { get; init; }
	public required string EntranceNoStep { get; init; }
	public required string SectionExtraCharge { get; init; }
	public required string SectionVia { get; init; }
	public required string ViaMinutes { get; init; }
	public required string ViaMinutesHint { get; init; }
	public required string ViaAny { get; init; }
	public required string ViaMinutesValue { get; init; }
	public required string ExtraChargeAny { get; init; }
	public required string ExtraChargeNone { get; init; }
	public required string ExtraChargeLocal { get; init; }
}

public sealed class ProviderStrings
{
	public required string Experimental { get; init; }
	public required string Title { get; init; }
	public required string Intro { get; init; }
	public required string InUse { get; init; }
	public required string Use { get; init; }
	public required string CapJourneys { get; init; }
	public required string CapPlaces { get; init; }
	public required string CapContinuation { get; init; }
	public required string CapTracking { get; init; }
	public required string CapOccupancy { get; init; }
	public required string CapPlatforms { get; init; }
	public required string CapRouting { get; init; }
	public required string CapDepartures { get; init; }
	public required string CapDisruptions { get; init; }
	public required string CapNetwork { get; init; }
	public required string CapExtras { get; init; }
	public required string Footer { get; init; }
}

public sealed class ExpertStrings
{
	public required string Open { get; init; }
	public required string Title { get; init; }
	public required string Copy { get; init; }
	public required string Copied { get; init; }
	public required string Journey { get; init; }
	public required string Leg { get; init; }
	public required string Stops { get; init; }
	public required string Transfers { get; init; }
	public required string Hint { get; init; }
}

public sealed class TrackingStrings
{
	public required string Title { get; init; }
	public required string Empty { get; init; }
	public required string EmptyHint { get; init; }
	public required string SectionActive { get; init; }
	public required string SectionPlanned { get; init; }
	public required string SectionRecent { get; init; }
	public required string SectionPaused { get; init; }
	public required string Pause { get; init; }
	public required string Resume { get; init; }
	public required string StopFollowing { get; init; }
	public required string DismissNotice { get; init; }
	public required string NoticeHistory { get; init; }
	public required string LiveTitle { get; init; }
	public required string LiveAutomatic { get; init; }
	public required string LiveAutomaticHint { get; init; }
	public required string LiveBadge { get; init; }
	public required string CourseShow { get; init; }
	public required string CourseHide { get; init; }
	public required string CourseNotYet { get; init; }
	public required string CourseWalk { get; init; }
	public required string CoursePlatform { get; init; }
	public required string CourseTrack { get; init; }
	public required string ShowOnMap { get; init; }
	public required string ToggleDetails { get; init; }
	public required string CourseWalkMinutes { get; init; }
	public required string CourseIn { get; init; }
	public required string CourseNow { get; init; }
	public required string DeleteAll { get; init; }
	public required string DeleteTitle { get; init; }
	public required string DeleteMessage { get; init; }
	public required string DeleteAllMessage { get; init; }
	public required string Alerts { get; init; }
	public required string AlertStart { get; init; }
	public required string AlertChange { get; init; }
	public required string AlertProblem { get; init; }
	public required string LeadMinutes { get; init; }
	public required string Periodic { get; init; }
	public required string NextStop { get; init; }
	public required string Departs { get; init; }
	public required string ArrivedAt { get; init; }
	public required string PhasePlanned { get; init; }
	public required string PhaseInProgress { get; init; }
	public required string PhaseAtInterchange { get; init; }
	public required string PhaseAtRisk { get; init; }
	public required string PhaseCancelled { get; init; }
	public required string PhaseArrived { get; init; }
	public required string PhasePaused { get; init; }
	public required string FollowedJourneys { get; init; }
	public required string Following { get; init; }
	public required string FollowFailed { get; init; }
	public required string NotificationsDenied { get; init; }
	public required string ChannelLive { get; init; }
	public required string ChannelAlerts { get; init; }
	public required string NotifStartsAt { get; init; }
	public required string NotifRoute { get; init; }
	public required string NotifRiding { get; init; }
	public required string NotifNext { get; init; }
	public required string NotifGetOff { get; init; }
	public required string NotifBoard { get; init; }
	public required string NotifChangeTitle { get; init; }
	public required string NotifWalkFromUntil { get; init; }
	public required string NotifChangeText { get; init; }
	public required string NotifRiskTitle { get; init; }
	public required string GuaranteedChange { get; init; }
	public required string NotifMissedText { get; init; }
	public required string NotifTightText { get; init; }
	public required string NotifCancelledTitle { get; init; }
	public required string NotifArrivedTitle { get; init; }
	public required string NotifDelay { get; init; }
	public required string NotifMonitoring { get; init; }
	public required string NotifActionPause { get; init; }
	public required string NotifActionStop { get; init; }
	public required string Replan { get; init; }
	public required string NotifStartAlertTitle { get; init; }
	public required string NotifStartAlertText { get; init; }
	public required string NotifChangeAlertTitle { get; init; }
	public required string NotifProblemAlertTitle { get; init; }
	public required string NotifMinutes { get; init; }
	public required string NotifNow { get; init; }
}

// <extras>
public sealed class ExtrasStrings
{
	public required string KindStop { get; init; }
	public required string KindAddress { get; init; }
	public required string KindPoi { get; init; }
	public required string SearchAddresses { get; init; }
	public required string SearchAddressesDescription { get; init; }
	public required string SearchPois { get; init; }
	public required string SearchPoisDescription { get; init; }
	public required string ExactPosition { get; init; }
	public required string ExactPositionDescription { get; init; }
	public required string LiveTitle { get; init; }
	public required string LiveShow { get; init; }
	public required string LineFilterPlaceholder { get; init; }
	public required string LiveStart { get; init; }
	public required string LiveStop { get; init; }
	public required string LiveConnecting { get; init; }
	public required string LiveWaiting { get; init; }
	public required string LiveRetry { get; init; }
	public required string LiveError { get; init; }
	public required string LiveLine { get; init; }
	public required string LiveRun { get; init; }
	public required string LiveSecondsAgo { get; init; }
	public required string LiveMinutesAgo { get; init; }
	public required string LiveHoursAgo { get; init; }
	public required string LiveOnTime { get; init; }
	public required string LiveSourceGps { get; init; }
	public required string LiveSourceTelegram { get; init; }
	public required string LiveHint { get; init; }
	public required string LiveOpenMap { get; init; }
	public required string AccessTitle { get; init; }
	public required string AccessHide { get; init; }
	public required string AccessNone { get; init; }
	public required string AccessPlatform { get; init; }
	public required string AccessBoarding { get; init; }
	public required string AccessKerb { get; init; }
	public required string AccessWidth { get; init; }
	public required string AccessTactile { get; init; }
	public required string AccessAudio { get; init; }
	public required string ServiceShow { get; init; }
	public required string ServiceHide { get; init; }
	public required string ServiceNone { get; init; }
	public required string ServiceNeedsPosition { get; init; }
	public required string CapLive { get; init; }
	public required string CapOpenData { get; init; }
	public required string CapFares { get; init; }
	public required string CapOptimisation { get; init; }
	public required string OptSection { get; init; }
	public required string OptFastest { get; init; }
	public required string OptFewestChanges { get; init; }
	public required string OptLeastWalking { get; init; }
	public required string OptLowestFare { get; init; }
	public required string OptNote { get; init; }
	public required string FaresTitle { get; init; }
	public required string FareSingle { get; init; }
	public required string FareDay { get; init; }
	public required string FareZones { get; init; }
	public required string FareValidFor { get; init; }
	public required string FareNotAll { get; init; }
	public required string FareBuy { get; init; }
	public required string ShortcutHome { get; init; }
	public required string ShortcutDepartures { get; init; }
	public required string ShortcutNoHome { get; init; }
	public required string FaresExpand { get; init; }
	public required string FaresCollapse { get; init; }
	public required string PassengerAdult { get; init; }
	public required string PassengerYouth { get; init; }
	public required string PassengerChild { get; init; }
	public required string PassengerSenior { get; init; }
	public required string PassengerDisabled { get; init; }
	public required string RunsOn { get; init; }
	public required string RunsDaily { get; init; }
	public required string VehicleReported { get; init; }
	public required string PdfFailed { get; init; }
	public required string LiveHowTitle { get; init; }
	public required string LiveHowText { get; init; }
	public required string LineFilterLabel { get; init; }
	public required string LiveInvalid { get; init; }
	public required string LiveEmpty { get; init; }
	public required string LiveCount { get; init; }
	public required string MapTitle { get; init; }
	public required string MapShow { get; init; }
	public required string MapKeyMissing { get; init; }
	public required string MapKeyInvalid { get; init; }
	public required string MapKeyOpenSettings { get; init; }
	public required string MapUnsupported { get; init; }
	public required string MapCrashed { get; init; }
	public required string MapBypass { get; init; }
	public required string MapUseLeaflet { get; init; }
	public required string MapPickStart { get; init; }
	public required string MapPickEnd { get; init; }
	public required string MapPickStop { get; init; }
	public required string MapPickOnMap { get; init; }
	public required string MapZoomHint { get; init; }
	public required string MapPickHint { get; init; }
	public required string MapStopDepartures { get; init; }
	public required string MapJourneyToHere { get; init; }
	public required string MapJourneyFromHere { get; init; }
	public required string MapUsePlace { get; init; }
	public required string MapNothingHere { get; init; }
	public required string MapJourneyTitle { get; init; }
	public required string MapStopTitle { get; init; }
	public required string MapNoData { get; init; }
	public required string JourneyRefreshFailed { get; init; }
	public required string MapStart { get; init; }
	public required string MapEnd { get; init; }
	public required string RunDeparted { get; init; }
	public required string MapAutoFit { get; init; }
	public required string MapFitNow { get; init; }
	public required string MapLayers { get; init; }
	public required string MapZones { get; init; }
	public required string MapParking { get; init; }
	public required string MapBikes { get; init; }
	public required string MapVehicles { get; init; }
	public required string VehicleSeen { get; init; }
	public required string VehicleFollowLine { get; init; }
	public required string RunWholeLine { get; init; }
	public required string LineCourseTitle { get; init; }
	public required string LineMapNone { get; init; }
	public required string BikesCounts { get; init; }
	public required string BikesDocks { get; init; }
	public required string BikesOpenApp { get; init; }
	public required string ParkingCounts { get; init; }
	public required string ParkingShort { get; init; }
	public required string ParkingUpdated { get; init; }
	public required string NetworkMap { get; init; }
	public required string NetworkMapRefresh { get; init; }
	public required string NetworkMapOpenPdf { get; init; }
	public required string NetworkMapCredit { get; init; }
	public required string NetworkMapStale { get; init; }
	public required string NetworkMapFailed { get; init; }
	public required string NetworkMapZoomIn { get; init; }
	public required string NetworkMapZoomOut { get; init; }
	public required string MapInfo { get; init; }
	public required string MapGrip { get; init; }
	public required string TrackFollowing { get; init; }
	public required string TrackNotFound { get; init; }
	public required string TrackNoCourse { get; init; }
	public required string TrackShowAll { get; init; }
	public required string TrackOnlyThisRun { get; init; }
	public required string NoticeClose { get; init; }
	public required string NoticeImageFailed { get; init; }
	public required string NoticeEmpty { get; init; }
	public required string NoticeLinkFailed { get; init; }
	public required string AroundTitle { get; init; }
	public required string LinesTitle { get; init; }
	public required string LinesCount { get; init; }
	public required string ServiceTitle { get; init; }
	public required string ServiceHint { get; init; }
	public required string ServiceCount { get; init; }
	public required string AccessHint { get; init; }
	public required string MapSummary { get; init; }
	public required string LiveSummary { get; init; }
	public required string HomeTitle { get; init; }
	public required string HomeNotSet { get; init; }
}
// </extras>


/// <summary>Home screen widgets: the texts on the widget and in its settings.</summary>
public sealed class WidgetStrings
{
	public required string NameRoute { get; init; }
	public required string NameDepartures { get; init; }
	public required string NameArrivals { get; init; }
	public required string NameNearby { get; init; }
	public required string NameNearbyDepartures { get; init; }
	public required string Here { get; init; }
	public required string Loading { get; init; }
	public required string SetUp { get; init; }
	public required string NoDepartures { get; init; }
	public required string NoArrivals { get; init; }
	public required string NoJourneys { get; init; }
	public required string NoStopsNearby { get; init; }
	public required string NeedsLocation { get; init; }
	public required string RefreshFailed { get; init; }
	public required string UpdatedAt { get; init; }
	public required string PositionFrom { get; init; }
	public required string Meters { get; init; }
	public required string ConfigTitle { get; init; }
	public required string ConfigFrom { get; init; }
	public required string ConfigTo { get; init; }
	public required string ConfigStop { get; init; }
	public required string ChoosePlace { get; init; }
	public required string SearchHint { get; init; }
	public required string NoResults { get; init; }
	public required string ConfigRadius { get; init; }
	public required string ConfigRows { get; init; }
	public required string RowsAuto { get; init; }
	public required string ConfigStops { get; init; }
	public required string ConfigPerStop { get; init; }
	public required string ConfigLines { get; init; }
	public required string LinesHint { get; init; }
	public required string ConfigModes { get; init; }
	public required string ModesAll { get; init; }
	public required string ConfigAuto { get; init; }
	public required string ConfigInterval { get; init; }
	public required string IntervalMinutes { get; init; }
	public required string IntervalHours { get; init; }
	public required string ConfigAutoHint { get; init; }
	public required string ConfigProvider { get; init; }
	public required string ConfigLabelTitle { get; init; }
	public required string ConfigTitleHint { get; init; }
	public required string ConfigProviderChanged { get; init; }
	public required string Done { get; init; }
	public required string LocationHint { get; init; }
	public required string OpenSettings { get; init; }
	public required string CardRefresh { get; init; }
	public required string CardOpen { get; init; }
	public required string CardSetUp { get; init; }
	public required string SetupTitle { get; init; }
	public required string SetupSummary { get; init; }
	public required string SetupHint { get; init; }
	public required string SetupPick { get; init; }
	public required string SetupNone { get; init; }
	public required string SetupKind { get; init; }
	public required string SetupKindDepartures { get; init; }
	public required string SetupKindRoute { get; init; }
	public required string SetupStop { get; init; }
	public required string SetupFrom { get; init; }
	public required string SetupTo { get; init; }
	public required string SetupSearch { get; init; }
	public required string SetupRows { get; init; }
	public required string SetupSave { get; init; }
	public required string SetupSaved { get; init; }
	public required string EditorFor { get; init; }
	public required string SetupNotChosen { get; init; }
	public required string SetupRowsCount { get; init; }
}
