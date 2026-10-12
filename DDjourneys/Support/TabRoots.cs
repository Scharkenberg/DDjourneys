using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Support;

/// <summary>
/// The phone's tab bar (see AppShell): on a phone the app opens with four tabs, everywhere else it is a single
/// door in which every page is pushed on top of the planner. This is the one place that lands on one of the four
/// ROOT pages (its default instance): on the phone the tab switches to it — an absolute "//" route, which also
/// unwinds that tab back to its root — everywhere else the page is pushed as these calls have always done.
/// A root's page type opened WITH parameters (a board for one stop, a map with a scene) never goes through here:
/// like every other page it is pushed on top of the active tab, never re-rooted.
/// </summary>
public static class TabRoots
{
	/// <summary>True on the phone form factor: the app shows the tab bar (see AppShell).</summary>
	public static bool Enabled =>
		DeviceInfo.Current.Idiom == DeviceIdiom.Phone;

	/// <summary>
	/// Lands on one of the root pages — <see cref="Routes.Plan"/>, <see cref="Routes.Departures"/>,
	/// <see cref="Routes.Map"/> or <see cref="Routes.Settings"/> (behind the fourth tab). Never throws.
	/// </summary>
	public static async Task OpenAsync(string route)
	{
		if (Shell.Current is not { } shell)
		{
			return;
		}

		try
		{
			if (Enabled
				&& (route == Routes.Plan
					|| route == Routes.Departures
					|| route == Routes.Map
					|| route == Routes.Settings))
			{
				// Settings lives behind the fourth tab; the other roots name their tab. Switching there with an
				// absolute route also unwinds that tab, so the default instance is what shows.
				string tab = route == Routes.Settings ? Routes.More : route;

				await shell.GoToAsync($"//{tab}");

				return;
			}

			// Everywhere else, and any other route: a plain push. The planner itself is never pushed onto itself;
			// asking for it means going back to the start.
			if (route == Routes.Plan)
			{
				await Panes.ToStartAsync(shell);

				return;
			}

			await shell.GoToAsync(route);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Opening '{route}' failed: {ex.Message}");
		}
	}
}
