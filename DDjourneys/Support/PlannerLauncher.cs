using DDjourneys.Contract;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Pages;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Support;

/// <summary>
/// Starts a journey from a place picked elsewhere (the map). The other end is the device position (the stop nearest
/// to it, else the last position the app took); without any position it is left open and the planner asks for it.
/// </summary>
public sealed class PlannerLauncher(
	DeviceLocator locator,
	LocationService locations)
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(8);

	/// <summary>Journey to this place, from where the passenger is.</summary>
	public async Task ToAsync(Location destination)
	{
		ArgumentNullException.ThrowIfNull(destination);

		Location? here = await NearMeAsync();

		await OpenAsync(
			new ResolvedPlan(here, destination, null, true, null, here is not null),
			askFor: here is null ? Ask.Start : Ask.None);
	}

	/// <summary>Journey from this place; the destination is asked for.</summary>
	public static Task FromAsync(Location origin)
	{
		ArgumentNullException.ThrowIfNull(origin);

		return OpenAsync(
			new ResolvedPlan(origin, null, null, true, null, false),
			askFor: Ask.Destination);
	}

	/// <summary>The stop nearest to the device, else nearest to the last known position, else null.</summary>
	public async Task<Location?> NearMeAsync()
	{
		try
		{
			using var cancel = new CancellationTokenSource(Wait);

			(double Latitude, double Longitude)? position =
				await locator.LocateAsync(cancel.Token);

			if (position is null
				&& DeviceLocator.LastFix() is { } last)
			{
				position = (last.Latitude, last.Longitude);
			}

			if (position is not { } here)
			{
				return null;
			}

			IReadOnlyList<Location> stops =
				await locations.SearchByCoordinatesAsync(here.Latitude, here.Longitude, timeout: Wait);

			return stops.FirstOrDefault(stop => stop.IsStation) ?? (stops.Count > 0 ? stops[0] : null);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Map] position for the journey failed: {ex.Message}");

			return null;
		}
	}

	private enum Ask
	{
		None,
		Start,
		Destination
	}

	private static async Task OpenAsync(ResolvedPlan plan, Ask askFor)
	{
		if (Shell.Current is not { } shell)
		{
			return;
		}

		await Panes.ToStartAsync(shell);

		if (shell.CurrentPage is not PlanPage page)
		{
			return;
		}

		page.ApplyContract(plan);

		switch (askFor)
		{
			case Ask.Start:
				page.PickStart();
				break;
			case Ask.Destination:
				page.PickDestination();
				break;
		}
	}
}
