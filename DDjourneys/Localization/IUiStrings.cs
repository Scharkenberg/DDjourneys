namespace DDjourneys.Localization;

public interface IUiStrings
{
	CommonStrings Common { get; }
	PlanStrings Plan { get; }
	PlaceSearchStrings PlaceSearch { get; }
	ResultsStrings Results { get; }
	JourneyStrings Journey { get; }
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
	public required string Refresh { get; init; }
	public required string Retry { get; init; }
	public required string Previous { get; init; }
	public required string Next { get; init; }
}
public sealed class JourneyStrings
{
	public required string Title { get; init; }
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
