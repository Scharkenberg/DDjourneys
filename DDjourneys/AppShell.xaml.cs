using DDjourneys.Pages;
using DDjourneys.Support;

namespace DDjourneys;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Pushed pages are routes only. Only the home page is a ShellContent.
		Routing.RegisterRoute(Routes.PlaceSearch, typeof(PlaceSearchPage));
		Routing.RegisterRoute(Routes.Results, typeof(ResultsPage));
		Routing.RegisterRoute(Routes.Journey, typeof(JourneyPage));
		Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
		Routing.RegisterRoute(Routes.Themes, typeof(ThemesPage));
		Routing.RegisterRoute(Routes.Tracked, typeof(TrackedJourneysPage));
		Routing.RegisterRoute(Routes.Expert, typeof(ExpertPage));
		Routing.RegisterRoute(Routes.Routing, typeof(RoutingSettingsPage));
		Routing.RegisterRoute(Routes.Providers, typeof(ProvidersPage));
	}
}