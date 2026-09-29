using DDjourneys.Core.Models;

namespace DDjourneys.Support;

/// <summary>One non-interactive mode chip on a journey card.</summary>
public sealed record LegChip(string Text, Color Color);

/// <summary>Mode colours are theme-independent tokens defined in Tokens.xaml.</summary>
public static class ModeColors
{
	public static Color For(TransitMode mode)
	{
		string key = mode switch
		{
			TransitMode.Tram => "ModeTram",
			TransitMode.Bus => "ModeBus",
			TransitMode.SuburbanRail => "ModeSuburban",
			TransitMode.Ferry => "ModeFerry",
			TransitMode.CableCar => "ModeCableCar",
			TransitMode.Walking => "ModeWalk",
			_ => "ModeTrain"
		};

		return Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color
			? color
			: Colors.Gray;
	}
}

/// <summary>Display-ready view of a Journey for the results list.</summary>
public sealed class JourneyCardModel
{
	public JourneyCardModel(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);
		Journey = journey;

		JourneyLeg? firstRide = journey.Legs.FirstOrDefault(l => l.Mode != TransitMode.Walking);
		JourneyLeg? lastRide = journey.Legs.LastOrDefault(l => l.Mode != TransitMode.Walking);

		DepartureTime = Format.Time(journey.Departure);
		ArrivalTime = Format.Time(journey.Arrival);
		DurationText = Format.Duration(journey.Duration);

		// Delays come from the rides: walking legs carry no realtime data.
		DepartureDelay = Format.Delay(firstRide?.DepartureDelay);
		ArrivalDelay = Format.Delay(lastRide?.ArrivalDelay);

		TransfersText = journey.TransferCount switch
		{
			0 => "Direct",
			1 => "1 transfer",
			int n => $"{n} transfers"
		};

		Chips = journey.Legs
			.Select(leg => new LegChip(
				leg.Mode == TransitMode.Walking
					? $"Walk {Format.Duration(leg.EffectiveArrival - leg.EffectiveDeparture)}"
					: leg.Line?.Name ?? leg.Mode.ToString(),
				ModeColors.For(leg.Mode)))
			.ToList();

		IsCancelled = journey.IsCancelled;
		IsOnTime = !journey.IsCancelled
			&& !journey.HasDelay
			&& journey.Legs.Any(l => l.RealtimeDeparture.HasValue || l.RealtimeArrival.HasValue);

		int notices = journey.Notices.Count
			+ journey.Legs.Sum(l => l.Notices.Count)
			+ journey.Transfers.Sum(t => t.Notices.Count);

		NoticesText = notices == 1 ? "1 notice" : $"{notices} notices";
		HasNotices = notices > 0;

		AccessibilityText =
			$"Departs {DepartureTime}, arrives {ArrivalTime}, {DurationText}, {TransfersText}"
			+ (IsCancelled ? ", cancelled" : string.Empty)
			+ (ArrivalDelay is { } late ? $", arrival {late}" : string.Empty)
			+ (HasNotices ? $", {NoticesText}" : string.Empty);
	}

	public Journey Journey { get; }

	public string DepartureTime { get; }
	public string ArrivalTime { get; }
	public string DurationText { get; }
	public string TransfersText { get; }

	public string? DepartureDelay { get; }
	public string? ArrivalDelay { get; }
	public bool HasDepartureDelay => DepartureDelay is not null;
	public bool HasArrivalDelay => ArrivalDelay is not null;

	public IReadOnlyList<LegChip> Chips { get; }

	public bool IsCancelled { get; }
	public bool IsOnTime { get; }
	public string NoticesText { get; }
	public bool HasNotices { get; }
	public bool HasStatusRow => IsCancelled || IsOnTime || HasNotices;

	public string AccessibilityText { get; }
}
