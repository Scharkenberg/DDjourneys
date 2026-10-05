using DDjourneys.Core.Widgets;

namespace DDjourneys.Platforms.Android.Widgets;

/// <summary>Java class names of the widget receivers and their settings screen (explicit, so the provider XML can name them).</summary>
internal static class WidgetNames
{
	public const string Route = "dev.scharkenberg.ddjourneys.widgets.RouteWidget";
	public const string Departures = "dev.scharkenberg.ddjourneys.widgets.DeparturesWidget";
	public const string Arrivals = "dev.scharkenberg.ddjourneys.widgets.ArrivalsWidget";
	public const string Nearby = "dev.scharkenberg.ddjourneys.widgets.NearbyWidget";
	public const string NearbyDepartures = "dev.scharkenberg.ddjourneys.widgets.NearbyDeparturesWidget";
	public const string Config = "dev.scharkenberg.ddjourneys.widgets.WidgetConfigActivity";

	/// <summary>Broadcast action of a tap on the widget surface.</summary>
	public const string Refresh = "dev.Scharkenberg.DDjourneys.widget.REFRESH";

	public static WidgetKind? KindOf(string? className) =>
		className switch
		{
			Route => WidgetKind.Route,
			Departures => WidgetKind.Departures,
			Arrivals => WidgetKind.Arrivals,
			Nearby => WidgetKind.NearbyStops,
			NearbyDepartures => WidgetKind.NearbyDepartures,
			_ => null
		};

	public static Type ProviderOf(WidgetKind kind) =>
		kind switch
		{
			WidgetKind.Route => typeof(RouteWidgetProvider),
			WidgetKind.Departures => typeof(DeparturesWidgetProvider),
			WidgetKind.Arrivals => typeof(ArrivalsWidgetProvider),
			WidgetKind.NearbyStops => typeof(NearbyWidgetProvider),
			_ => typeof(NearbyDeparturesWidgetProvider)
		};
}
