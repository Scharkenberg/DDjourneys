using DDjourneys.Core.Models;
using System.Text.Json;

namespace DDjourneys.Platforms.Android.LiveJourney.Schutzengel;

/// <summary>Maps the app journey into the service's multi-leg plan shape.</summary>
internal static class SchutzengelPlanTranslator
{
	public static object Translate(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);
		return new
		{
			client_journey_id = journey.Id ?? BuildStableIdentity(journey),
			origin = Place(journey.From),
			destination = Place(journey.To),
			departure = journey.Departure?.ToUniversalTime().ToString("O"),
			arrival = journey.Arrival?.ToUniversalTime().ToString("O"),
			legs = journey.Legs.Select((leg, index) => new
			{
				order = index,
				mode = leg.Mode.ToString(),
				line = leg.Line?.Name,
				direction = leg.Line?.Destination,
				from = Place(leg.From),
				to = Place(leg.To),
				departure = leg.ScheduledDeparture?.ToUniversalTime().ToString("O"),
				arrival = leg.ScheduledArrival?.ToUniversalTime().ToString("O"),
				stops = leg.Stops.Select(s => new
				{
					id = s.Station.Id,
					name = s.Station.Name,
					place = s.Station.Place,
					arrival = s.ScheduledArrival?.ToUniversalTime().ToString("O"),
					departure = s.ScheduledDeparture?.ToUniversalTime().ToString("O"),
					cancelled = s.IsCancelled
				}),
				cancelled = leg.IsCancelled,
				notices = leg.Notices
			}),
			interchanges = journey.Transfers.Select(t => new
			{
				location = Place(t.Location),
				previous_leg = t.PreviousLegIndex,
				next_leg = t.NextLegIndex,
				seconds = (long)t.Duration.TotalSeconds,
				waiting_seconds = t.WaitingTime is null ? (long?)null : (long)t.WaitingTime.Value.TotalSeconds,
				guaranteed = t.IsGuaranteed,
				notices = t.Notices
			}),
			notices = journey.Notices,
			initial_risk_hints = new
			{
				cancelled = journey.IsCancelled,
				transfer_count = journey.TransferCount,
				at_risk_transfers = journey.Transfers.Count(t => !t.IsGuaranteed),
				leg_notices = journey.Legs.SelectMany((l, i) => l.Notices.Select(n => new { leg = i, text = n }))
			}
		};
	}

	public static string Serialize(Journey journey) => JsonSerializer.Serialize(Translate(journey));

	private static object Place(Station station) => new { id = station.Id, name = station.Name, place = station.Place, latitude = station.Latitude, longitude = station.Longitude };
	private static string BuildStableIdentity(Journey journey) => $"{journey.From.Id}:{journey.To.Id}:{journey.Departure:O}:{journey.Arrival:O}";
}
