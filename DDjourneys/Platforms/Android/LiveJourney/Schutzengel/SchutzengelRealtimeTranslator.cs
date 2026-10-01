using System.Text.RegularExpressions;
using DDjourneys.Core.Tracking;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

internal static class SchutzengelRealtimeTranslator
{
	public static TrackingPhase Translate(string realtimeAndNotifications, out JourneyTrackingEventKind kind, out string? message)
	{
		var text = realtimeAndNotifications.ToLowerInvariant();
		if (Regex.IsMatch(text, @"\b(cancelled|canceled|trip_cancelled)\b") || Regex.IsMatch(text, @"""tripcancelled""\s*:\s*true"))
		{
			kind = JourneyTrackingEventKind.Cancelled;
			message = "Journey cancelled";
			return TrackingPhase.Cancelled;
		}
		if (Regex.IsMatch(text, @"\b(arrived|journey complete|trip complete|completed)\b"))
		{
			kind = JourneyTrackingEventKind.Arrived;
			message = "Arrived";
			return TrackingPhase.Arrived;
		}
		if (Regex.IsMatch(text, @"\b(missed connection|changeover endangered|at risk|endangered)\b") || Regex.IsMatch(text, @"""changeoverendangered""\s*:\s*true"))
		{
			kind = JourneyTrackingEventKind.RiskChanged;
			message = "Connection at risk";
			return TrackingPhase.AtRisk;
		}
		kind = JourneyTrackingEventKind.Updated;
		message = null;
		return TrackingPhase.InProgress;
	}
}
