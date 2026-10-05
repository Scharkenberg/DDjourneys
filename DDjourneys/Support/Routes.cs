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

	/// <summary>The settings page for starting the app in input mode.</summary>
	public const string StartSettings = "startsettings";
	public const string Providers = "providers";
	public const string Departures = "departures";
	public const string Run = "run";
	public const string Disruptions = "disruptions";
	public const string Vehicles = "vehicles";
	public const string StopInfo = "stopinfo";

	/// <summary>The map page, and its query keys: a <c>MapScene</c> and the page title.</summary>
	public const string Map = "map";
	public const string MapScene = "MapScene";
	public const string MapTitle = "MapTitle";

	/// <summary>
	/// Query key: how the map page is used. <see cref="MapModePick"/> answers with a place like the place search
	/// (<see cref="SelectedPlace"/>, <see cref="TargetIsFrom"/>, <see cref="Target"/>); without a scene the map explores the stops around.
	/// </summary>
	public const string MapMode = "MapMode";
	public const string MapModePick = "pick";

	/// <summary>Query key: a <c>Location</c> the map opens centred on (instead of the device, the last stop, the provider's city).</summary>
	public const string MapAt = "MapAt";

	/// <summary>Query keys of the departures page: show arrivals (bool) and the time (DateTime, provider time).</summary>
	public const string BoardArrivals = "BoardArrivals";
	public const string BoardTime = "BoardTime";

	/// <summary>Query key: a <c>TrackTarget</c>, the one run the live vehicles page follows.</summary>
	public const string Track = "Track";

	/// <summary>Query key: a list of <c>TrackTarget</c>, the runs of a followed journey the live vehicles page follows together.</summary>
	public const string TrackSet = "TrackSet";

	/// <summary>Query key: line number(s) the live vehicles page opens with.</summary>
	public const string Line = "Line";

	/// <summary>
	/// Query key: what a place search is for when it is not the start or the destination
	/// (<see cref="TargetVia"/>, <see cref="TargetDepartures"/>). Answered with the chosen place under <see cref="SelectedPlace"/>.
	/// </summary>
	public const string Target = "Target";
	public const string TargetVia = "via";
	public const string TargetDepartures = "departures";

	/// <summary>The place search is for the default start of the input mode (any stop, address or point of interest).</summary>
	public const string TargetStart = "start";

	/// <summary>Query key: a stop (Location) the page is opened for.</summary>
	public const string Stop = "Stop";

	/// <summary>Query key: a departure (the run page).</summary>
	public const string DepartureData = "DepartureData";

	/// <summary>Query key: ids of route changes the disruptions page is limited to (comma separated).</summary>
	public const string ChangeIds = "ChangeIds";

	/// <summary>Query key: line name the disruptions page is filtered to.</summary>
	public const string LineName = "LineName";

	/// <summary>The detail page of one disruption or network notice, and its query key.</summary>
	public const string Disruption = "disruption";
	public const string DisruptionData = "DisruptionData";
}