namespace DDjourneys.Localization;

public interface IUiStrings
{
	CommonStrings Common { get; }
	PlanStrings Plan { get; }
	PlaceSearchStrings PlaceSearch { get; }
	ResultsStrings Results { get; }
	JourneyStrings Journey { get; }
	TrackingStrings Tracking { get; }
	ExpertStrings Expert { get; }
	ProviderStrings Provider { get; }
	RoutingStrings Routing { get; }
	SettingsStrings Settings { get; }
	TransportStrings Transport { get; }
}

public sealed class CommonStrings
{
	public required string Ok { get; init; }
	public required string Cancel { get; init; }
	public required string Retry { get; init; }
	public required string Close { get; init; }
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
}

public sealed class PlaceSearchStrings
{
	public required string Title { get; init; }
	public required string SearchPlaceholder { get; init; }
	public required string SearchForPlace { get; init; }
	public required string Hint { get; init; }
	public required string TypeAtLeastTwoCharacters { get; init; }
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
	public required string About { get; init; }
	public required string ThemeSystem { get; init; }
	public required string ThemeSystemDescription { get; init; }
	public required string ThemeLight { get; init; }
	public required string ThemeLightDescription { get; init; }
	public required string ThemeDark { get; init; }
	public required string ThemeDarkDescription { get; init; }
	public required string ThemeAmoled { get; init; }
	public required string ThemeAmoledDescription { get; init; }
	public required string ThemeOther { get; init; }
	public required string ThemeOtherDescription { get; init; }
	public required string ThemesTitle { get; init; }
	public required string ThemesLightHeader { get; init; }
	public required string ThemesDarkHeader { get; init; }
	public required string Animations { get; init; }
	public required string AnimationsDescription { get; init; }
	public required string TechnicalDetails { get; init; }
	public required string TechnicalDetailsDescription { get; init; }
	public required string Results { get; init; }
	public required string ResultsDescription { get; init; }
	public required string RequestTimeout { get; init; }
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
	public required string PlaceSearch { get; init; }
	public required string SearchDelay { get; init; }
	public required string SearchDelayDescription { get; init; }
	public required string MinQueryLength { get; init; }
	public required string MinQueryLengthDescription { get; init; }
	public required string RoutingEntry { get; init; }
	public required string RoutingEntryDescription { get; init; }
	public required string VersionPrefix { get; init; }
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
}

public sealed class ProviderStrings
{
	public required string Title { get; init; }
	public required string Intro { get; init; }
	public required string InUse { get; init; }
	public required string CapJourneys { get; init; }
	public required string CapPlaces { get; init; }
	public required string CapContinuation { get; init; }
	public required string CapTracking { get; init; }
	public required string CapOccupancy { get; init; }
	public required string CapPlatforms { get; init; }
	public required string CapRouting { get; init; }
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
	public required string LiveTitle { get; init; }
	public required string LiveAutomatic { get; init; }
	public required string LiveAutomaticHint { get; init; }
	public required string LiveBadge { get; init; }
	public required string CourseShow { get; init; }
	public required string CourseHide { get; init; }
	public required string CourseNotYet { get; init; }
	public required string CourseWalk { get; init; }
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
	public required string NotifChangeTitle { get; init; }
	public required string NotifChangeText { get; init; }
	public required string NotifRiskTitle { get; init; }
	public required string NotifMissedText { get; init; }
	public required string NotifTightText { get; init; }
	public required string NotifCancelledTitle { get; init; }
	public required string NotifArrivedTitle { get; init; }
	public required string NotifDelay { get; init; }
	public required string NotifMonitoring { get; init; }
	public required string NotifActionPause { get; init; }
	public required string NotifActionStop { get; init; }
	public required string NotifStartAlertTitle { get; init; }
	public required string NotifStartAlertText { get; init; }
	public required string NotifChangeAlertTitle { get; init; }
	public required string NotifProblemAlertTitle { get; init; }
	public required string NotifMinutes { get; init; }
	public required string NotifNow { get; init; }
}
