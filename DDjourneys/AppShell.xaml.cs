using DDjourneys.Core.Diagnostics;
using DDjourneys.Pages;
using DDjourneys.Support;

namespace DDjourneys;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Up and showing a page for a few seconds: this start counts as finished (see StartupGuard).
		Navigated += OnFirstNavigated;

		// Pushed pages are routes only. Only the home page is a ShellContent.
		Routing.RegisterRoute(Routes.PlaceSearch, typeof(PlaceSearchPage));
		Routing.RegisterRoute(Routes.Results, typeof(ResultsPage));
		Routing.RegisterRoute(Routes.Journey, typeof(JourneyPage));
		Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
		Routing.RegisterRoute(Routes.Appearance, typeof(AppearancePage));
		Routing.RegisterRoute(Routes.Tracked, typeof(TrackedJourneysPage));
		Routing.RegisterRoute(Routes.Expert, typeof(ExpertPage));
		Routing.RegisterRoute(Routes.Routing, typeof(RoutingSettingsPage));
		Routing.RegisterRoute(Routes.StartSettings, typeof(StartSettingsPage));
		Routing.RegisterRoute(Routes.Providers, typeof(ProvidersPage));
		Routing.RegisterRoute(Routes.About, typeof(AboutPage));
		Routing.RegisterRoute(Routes.Departures, typeof(DeparturesPage));
		Routing.RegisterRoute(Routes.Run, typeof(RunPage));
		Routing.RegisterRoute(Routes.Disruptions, typeof(DisruptionsPage));
		Routing.RegisterRoute(Routes.Disruption, typeof(DisruptionPage));
		Routing.RegisterRoute(Routes.Vehicles, typeof(VehiclesPage));
		Routing.RegisterRoute(Routes.Map, typeof(MapPage));
		Routing.RegisterRoute(Routes.NetworkMap, typeof(NetworkMapPage));
#if WINDOWS
		Routing.RegisterRoute(Routes.WidgetSetup, typeof(WidgetSetupPage));
#endif

		Navigating += OnNavigating;

		// Whether Back closes a pane depends on the page in front (Android's back callback follows it).
		Navigated += (_, _) => Panes.NotifyBackChanged();
	}

	private void OnFirstNavigated(object? sender, ShellNavigatedEventArgs e)
	{
		Navigated -= OnFirstNavigated;

		Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), StartupGuard.Complete);
	}

	/// <summary>
	/// Page transition, exit half: the page that is left slides away and fades while the shell switches (the entrance
	/// half is <see cref="Motion.EnterPage"/>: a fade-through). Deliberately no navigation deferral: the shell throws
	/// when a second navigation starts while one is deferred, and a double tap must stay harmless.
	/// </summary>
	private void OnNavigating(object? sender, ShellNavigatingEventArgs e)
	{
		// Pages moving between the Shell stack and panes (the window width changed) are no navigation the user made.
		if (Panes.IsRearranging)
		{
			return;
		}

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
			DiagnosticLog.Write($"Page transition skipped: {ex.Message}");
		}
	}
}
