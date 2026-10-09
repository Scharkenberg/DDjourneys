namespace DDjourneys.Support;

/// <summary>
/// Whether the app's window is on screen at all. Pages stop their timers in <c>OnDisappearing</c>, but a minimised or
/// hidden window (Windows) raises none, so the periodic work of a page that is still "open" (departures every 30 s,
/// the journey clock, vehicle ticks, the followed-journeys loops) asks here first. Android and the like stay "shown":
/// their pages are told when the app goes to the background.
/// </summary>
public static class AppVisibility
{
	public static bool IsShown { get; private set; } = true;

	/// <summary>Raised when the window was hidden or minimised, or came back.</summary>
	public static event EventHandler? Changed;

	public static void Set(bool shown)
	{
		if (IsShown == shown)
		{
			return;
		}

		IsShown = shown;
		Changed?.Invoke(null, EventArgs.Empty);
	}
}
