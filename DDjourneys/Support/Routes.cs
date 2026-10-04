namespace DDjourneys.Support;

/// <summary>
/// Shell route names. Pages are registered as they are added.
/// </summary>
public static class Routes
{
	public const string Plan = "plan";
	public const string PlaceSearch = "placesearch";
	public const string TargetIsFrom = "TargetIsFrom";
	public const string SelectedPlace = "SelectedPlace";
	public const string Query = "Query";
	public const string JourneyData = "JourneyData";
	public const string Results = "results";
	public const string Journey = "journey";
	public const string Settings = "settings";
	public const string Appearance = "appearance";
	public const string Tracked = "tracked";

	/// <summary>Query key: plan id of the followed journey to bring into view.</summary>
	public const string FocusPlan = "FocusPlan";
	public const string Expert = "expert";
	public const string Routing = "routing";
	public const string Providers = "providers";
	public const string Departures = "departures";
	public const string Run = "run";
	public const string Disruptions = "disruptions";

	/// <summary>
	/// Query key: what a place search is for when it is not the start or the destination
	/// (<see cref="TargetVia"/>, <see cref="TargetDepartures"/>). Answered with the chosen place under <see cref="SelectedPlace"/>.
	/// </summary>
	public const string Target = "Target";
	public const string TargetVia = "via";
	public const string TargetDepartures = "departures";

	/// <summary>Query key: a stop (Location) the page is opened for.</summary>
	public const string Stop = "Stop";

	/// <summary>Query key: a departure (the run page).</summary>
	public const string DepartureData = "DepartureData";

	/// <summary>Query key: ids of route changes the disruptions page is limited to (comma separated).</summary>
	public const string ChangeIds = "ChangeIds";
}