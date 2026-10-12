using DDjourneys.Core.Diagnostics;

namespace DDjourneys.Pages;

public sealed partial class DeparturesViewModel
{
	public Command<StopLineRow> ToggleWatchCommand { get; } = new Command<StopLineRow>(ToggleWatchLine);

	private void ToggleWatchLine(StopLineRow? row)
	{
		if (row is null || Stop is not { } stop || string.IsNullOrWhiteSpace(stop.ProviderId))
		{
			return;
		}

		try
		{
			bool watched = WatchedLines.Toggle(stop.ProviderId, row.Line.Name);
			row.IsWatched = watched;
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"Toggling the watched state of '{row.Line.Name}' failed: {ex.Message}");
		}
	}
}
