using DDjourneys.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace DDjourneys.Support;

/// <summary>
/// Which pages may stand side by side in a wide window, and from which width. A pair is (left route, right route):
/// the right page is the one opened from the left page. Everything not listed opens as an ordinary Shell page.
/// </summary>
public static class PaneRules
{
	/// <summary>Width (dp) one pane needs: 720 dp give two panes (a phone in landscape included), 1080 three, and so on.</summary>
	public const double MinPaneWidth = 360;

	/// <summary>A pane count is kept until the width is this much below its limit, so a width near a limit does not flip back and forth.</summary>
	public const double Hysteresis = 40;

	/// <summary>Never more panes than this side by side (the flows are never deeper).</summary>
	public const int MaxPanes = 5;

	/// <summary>How many panes fit side by side in <paramref name="width"/>, given the count shown now.</summary>
	public static int Columns(double width, int current)
	{
		int fits = Math.Clamp((int)(width / MinPaneWidth), 1, MaxPanes);

		return current > fits && current <= MaxPanes && width >= (current * MinPaneWidth) - Hysteresis
			? current
			: fits;
	}

	/// <summary>Panes in one area of a folded display (at least one: each side of a fold gets a pane).</summary>
	public static int Region(double width) =>
		Math.Clamp((int)(width / MinPaneWidth), 1, MaxPanes);

	private static readonly Dictionary<string, Type> PageTypes =
		new()
		{
			[Routes.Plan] = typeof(PlanPage),
			[Routes.Results] = typeof(ResultsPage),
			[Routes.Journey] = typeof(JourneyPage),
			[Routes.Map] = typeof(MapPage),
			[Routes.Departures] = typeof(DeparturesPage),
			[Routes.Run] = typeof(RunPage),
			[Routes.Vehicles] = typeof(VehiclesPage),
			[Routes.Tracked] = typeof(TrackedJourneysPage)
		};

	private static readonly HashSet<(string Left, string Right)> Pairs =
	[
		(Routes.Plan, Routes.Results),
		(Routes.Results, Routes.Journey),
		(Routes.Journey, Routes.Map),
		(Routes.Journey, Routes.Vehicles),
		(Routes.Departures, Routes.Run),
		(Routes.Run, Routes.Map),
		(Routes.Run, Routes.Vehicles),
		(Routes.Plan, Routes.Tracked),
		(Routes.Journey, Routes.Tracked)
	];

	public static Type? PageFor(string route) =>
		PageTypes.GetValueOrDefault(route);

	public static string? RouteOf(Type type)
	{
		foreach ((string route, Type page) in PageTypes)
		{
			if (page == type)
			{
				return route;
			}
		}

		return null;
	}

	/// <summary>A new instance of a page on the old one's view model (no reflection: the constructors are named here).</summary>
	internal static PanePage? Recreate(PanePage old, IServiceProvider services) =>
		old switch
		{
			PlanPage { BindingContext: PlanViewModel vm } => new PlanPage(vm, services.GetRequiredService<AppSettings>()),
			ResultsPage { BindingContext: ResultsViewModel vm } => new ResultsPage(vm),
			JourneyPage { BindingContext: JourneyViewModel vm } => new JourneyPage(vm),
			DeparturesPage { BindingContext: DeparturesViewModel vm } => new DeparturesPage(vm),
			RunPage { BindingContext: RunViewModel vm } => new RunPage(vm),
			VehiclesPage { BindingContext: VehiclesViewModel vm } => new VehiclesPage(vm),
			TrackedJourneysPage { BindingContext: TrackedJourneysViewModel vm } => new TrackedJourneysPage(vm),
			MapPage => services.GetService<MapPage>(),
			_ => null
		};

	public static bool CanPair(string? left, string? right) =>
		left is not null
		&& right is not null
		&& Pairs.Contains((left, right));
}
