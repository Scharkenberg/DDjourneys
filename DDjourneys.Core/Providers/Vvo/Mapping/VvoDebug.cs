using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers.Vvo.Models;

namespace DDjourneys.Core.Providers.Vvo.Mapping;

internal static class VvoDebug
{
	public static void DumpRoute(VvoRoute route)
	{
		if (!DiagnosticLog.Enabled)
		{
			return;
		}

		DiagnosticLog.Write(
			$"========== VVO ROUTE Duration={route.Duration} ==========");

		foreach (var pr in route.PartialRoutes)
		{
			var mot = pr.Mot;

			DiagnosticLog.Write(
				$"""
				PARTIAL
				  Id={pr.PartialRouteId}
				  Duration={pr.Duration}
				  Mot={mot?.Type} {mot?.Name}
				  Info={string.Join("|", pr.Infos)}
				""");

			var stops = pr.RegularStops;

			if (stops.Count > 0)
			{
				var first = stops[0];
				var last = stops[^1];

				DiagnosticLog.Write(
					$"""
					  FIRST:
					    {first.Name}
					    Arr={first.ArrivalTime}
					    Dep={first.DepartureTime}
					    Platform={first.Platform?.Name}

					  LAST:
					    {last.Name}
					    Arr={last.ArrivalTime}
					    Dep={last.DepartureTime}
					    Platform={last.Platform?.Name}
					""");
			}
			else
			{
				DiagnosticLog.Write(
					"  NO STOPS");
			}
		}
	}
}