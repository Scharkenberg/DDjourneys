using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;

namespace DDjourneys.Pages;

public sealed partial class DeparturesViewModel
{
	/// <summary>Created on first use: a partial file cannot set a get-only property from the main file's constructor.</summary>
	public Command<StopLineRow> ToggleWatchCommand =>
		field ??= new Command<StopLineRow>(ToggleWatchLine);

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
