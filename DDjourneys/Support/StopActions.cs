using DDjourneys.Localization;
using DDjourneys.Pages;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support;

/// <summary>What can be done at a stop that a map or a plan shows.</summary>
public enum StopAction
{
	None,
	Departures,
	JourneyTo,
	JourneyFrom,
	Share
}

/// <summary>
/// The one stop sheet of every surface that shows stops (the explore map, the plan pages): departures, a journey
/// to or from it, sharing it. Asking and doing are apart, so a page can find the stop between the two.
/// </summary>
public static class StopActions
{
	/// <summary>Asks on <paramref name="dialogs"/> (a lent pane passes its host).</summary>
	public static async Task<StopAction> AskAsync(Page dialogs, string title)
	{
		IUiStrings strings = LocalizationService.Current.CurrentStrings;
		ExtrasStrings extras = strings.Extras;

		string choice =
			await dialogs.DisplayActionSheetAsync(
				title,
				strings.Common.Cancel,
				null,
				extras.MapStopDepartures,
				extras.MapJourneyToHere,
				extras.MapJourneyFromHere,
				extras.ShareStop);

		return choice == extras.MapStopDepartures ? StopAction.Departures
			: choice == extras.MapJourneyToHere ? StopAction.JourneyTo
			: choice == extras.MapJourneyFromHere ? StopAction.JourneyFrom
			: choice == extras.ShareStop ? StopAction.Share
			: StopAction.None;
	}

	public static Task RunAsync(StopAction action, Location stop, Page dialogs, PlannerLauncher planner) =>
		action switch
		{
			StopAction.Departures => DeparturesAsync(stop),
			StopAction.JourneyTo => planner.ToAsync(stop),
			StopAction.JourneyFrom => PlannerLauncher.FromAsync(stop),
			StopAction.Share => StopSharePopup.ShowAsync(dialogs, stop),
			_ => Task.CompletedTask
		};

	/// <summary>The departures of the stop, pushed on top of the page in front.</summary>
	public static Task DeparturesAsync(Location stop) =>
		Shell.Current.GoToAsync(
			Routes.Departures,
			new ShellNavigationQueryParameters
			{
				[Routes.Stop] = stop
			});
}
