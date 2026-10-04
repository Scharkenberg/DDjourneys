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
		Routing.RegisterRoute(Routes.Appearance, typeof(AppearancePage));
		Routing.RegisterRoute(Routes.Tracked, typeof(TrackedJourneysPage));
		Routing.RegisterRoute(Routes.Expert, typeof(ExpertPage));
		Routing.RegisterRoute(Routes.Routing, typeof(RoutingSettingsPage));
		Routing.RegisterRoute(Routes.Providers, typeof(ProvidersPage));
		Routing.RegisterRoute(Routes.Departures, typeof(DeparturesPage));
		Routing.RegisterRoute(Routes.Run, typeof(RunPage));
		Routing.RegisterRoute(Routes.Disruptions, typeof(DisruptionsPage));
		Routing.RegisterRoute(Routes.Disruption, typeof(DisruptionPage));
		Routing.RegisterRoute(Routes.Vehicles, typeof(VehiclesPage));
		Routing.RegisterRoute(Routes.Map, typeof(MapPage));

		Navigating += OnNavigating;
	}

	/// <summary>
	/// Page transition, exit half: the page that is left slides away and fades while the shell switches (the entrance
	/// half is <see cref="Motion.EnterPage"/>: a fade-through). Deliberately no navigation deferral: the shell throws
	/// when a second navigation starts while one is deferred, and a double tap must stay harmless.
	/// </summary>
	private void OnNavigating(object? sender, ShellNavigatingEventArgs e)
	{
		try
		{
			bool back = e.Source is ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot;
			bool forward = e.Source is ShellNavigationSource.Push;

			if (!Motion.Enabled || e.Cancelled || !(back || forward))
			{
				return;
			}

			Motion.NavigationStarting(back);

			if (CurrentPage is not ContentPage leaving)
			{
				return;
			}

			_ = Motion.ExitPageAsync(leaving, back);

			// If the navigation was cancelled the page is still the current one: show it again.
			leaving.Dispatcher.DispatchDelayed(
				TimeSpan.FromMilliseconds(1200),
				() =>
				{
					if (CurrentPage == leaving)
					{
						Motion.Restore(leaving);
					}
				});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Page transition skipped: {ex.Message}");
		}
	}
}
