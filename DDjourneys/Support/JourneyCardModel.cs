using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Localization;

namespace DDjourneys.Support;

/// <summary>One non-interactive mode chip on a journey card.</summary>
public sealed record LegChip(string Text, ChipLook Look);

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
			TransitMode.Walk => "ModeWalk",
			TransitMode.Bike => "ModeBike",
			_ => "ModeTrain"
		};

		return Application.Current?.Resources.TryGetValue(
			key,
			out object? value) == true
			&& value is Color color
			? color
			: Colors.Gray;
	}
}

/// <summary>Display-ready view of a Journey for the results list.</summary>
public sealed partial class JourneyCardModel : ObservableObject
{
	private readonly LocalizationService _localization;

	private string _transfersText = string.Empty;
	private string _blockText = string.Empty;
	private readonly JourneyBlock? _block;
	private IReadOnlyList<LegChip> _chips = [];
	private string _noticesText = string.Empty;
	private bool _hasNotices;
	private string _accessibilityText = string.Empty;
	private string _nextStepText = string.Empty;

	/// <summary>
	/// Set once the entrance animation ran, so recycled cards never replay it while scrolling.
	/// </summary>
	public bool Revealed { get; set; }

	/// <summary>
	/// B11's simple journey card: the time and only the next step, in huge type. A verbosity flag, not a
	/// fork: the same card control renders both shapes.
	/// </summary>
	public bool Simple { get; set; }

	/// <summary>The ordinary card (the inverse of <see cref="Simple"/>, for XAML visibility).</summary>
	public bool Detailed => !Simple;

	/// <summary>The one next step of the journey ("Walk 4 min · to Postplatz", "61 · to Pirnaischer Platz").</summary>
	public string NextStepText => _nextStepText;

	public bool HasNextStep =>
		_nextStepText.Length > 0;

	public JourneyCardModel(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		Journey = journey;
		_localization = LocalizationService.Current;

		JourneyLeg? firstRide =
			journey.Legs.FirstOrDefault(
				l => l.IsRide);

		JourneyLeg? lastRide =
			journey.Legs.LastOrDefault(
				l => l.IsRide);

		// The place shown under each time is where the passenger is at that moment: the searched
		// origin/destination when a walk leads to/from the first/last vehicle, else the stop itself.
		Station start =
			journey.AccessDuration > TimeSpan.Zero
			&& journey.Origin is { } origin
				? origin
				: journey.From;

		Station end =
			journey.EgressDuration > TimeSpan.Zero
			&& journey.Destination is { } destination
				? destination
				: journey.To;

		FromName = start.Name;
		FromPlace = StopLabel.PlaceFor(start.Name, start.Place);
		ToName = end.Name;
		ToPlace = StopLabel.PlaceFor(end.Name, end.Place);

		DepartureTime = Format.TimeOrDash(journey.Departure);
		ArrivalTime = Format.TimeOrDash(journey.Arrival);
		DurationText = Format.Duration(journey.Duration);

		// Delays come from the rides: walking legs carry no realtime data.
		DepartureDelay = Format.Delay(firstRide?.DepartureDelay);
		ArrivalDelay = Format.Delay(lastRide?.ArrivalDelay);

		// "Cancelled" on the card means "cannot take place": also a skipped boarding/alighting stop
		// or a connection the real-time data makes unreachable (see JourneyFeasibility).
		_block = journey.Block;
		IsCancelled = _block is not null;
		IsOnTime =
			!IsCancelled
			&& !journey.HasDelay
			&& journey.Legs.Any(
				l => l.RealtimeDeparture.HasValue
					|| l.RealtimeArrival.HasValue);

		int notices =
			journey.Notices.Count
			+ journey.Legs.Sum(l => l.Notices.Count)
			+ journey.Transfers.Sum(t => t.Notices.Count);

		_hasNotices = notices > 0;
		HasNotices = _hasNotices;
		NoticeCount = notices;

		RebuildLocalizedValues(notices);
	}

	public Journey Journey { get; }

	/// <summary>Whether the card names where the journey starts and ends (the detail page already does).</summary>
	public bool ShowEndpoints { get; init; } = true;

	public string FromName { get; }
	public string? FromPlace { get; }
	public string ToName { get; }
	public string? ToPlace { get; }

	public string DepartureTime { get; }
	public string ArrivalTime { get; }
	public string DurationText { get; }

	public string TransfersText => _transfersText;

	/// <summary>Who the price is for (the option "Tickets for"); the normal adult price by default.</summary>
	public PassengerCategory Passenger { get; init; } = PassengerCategory.Adult;

	/// <summary>Opens the journey on the map; null on cards that have no map button (the results list).</summary>
	public System.Windows.Input.ICommand? MapCommand { get; init; }

	public bool HasMap =>
		MapCommand is not null;

	/// <summary>The single ticket for <see cref="Passenger"/> ("2,70 €"), or null when the provider quotes none.</summary>
	public string? PriceText
	{
		get
		{
			JourneyFare? fare =
				FareChoice.Preferred(
					Journey.Fares,
					Passenger);

			return fare is { Price: { } price }
				? Format.Price(price, fare.Currency)
				: null;
		}
	}

	public bool HasPrice =>
		PriceText is not null;

	public string? DepartureDelay { get; }
	public string? ArrivalDelay { get; }

	public bool HasDepartureDelay =>
		DepartureDelay is not null;

	public bool HasArrivalDelay =>
		ArrivalDelay is not null;

	public IReadOnlyList<LegChip> Chips => _chips;

	public bool IsCancelled { get; }
	public bool IsOnTime { get; }

	/// <summary>Why the journey cannot take place ("Not possible · Schweriner Straße is not served").</summary>
	public string BlockText => _blockText;

	public string NoticesText => _noticesText;
	public bool HasNotices { get; }

	/// <summary>How many notices the journey carries; the card shows it as a small badge, not as a line of its own.</summary>
	public int NoticeCount { get; }

	/// <summary>The line under the legs: only "on time" is worth a line; notices are a badge.</summary>
	public bool HasStatusRow =>
		Detailed && IsOnTime;

	/// <summary>The icons next to the map icon (journey page); null on the cards of the result list.</summary>
	public JourneyActions? Actions { get; init; }

	public string AccessibilityText =>
		_accessibilityText;

	/// <summary>
	/// Rebuilds the localized display strings after the application language changes.
	/// </summary>
	public void RefreshLocalization()
	{
		int notices =
			Journey.Notices.Count
			+ Journey.Legs.Sum(l => l.Notices.Count)
			+ Journey.Transfers.Sum(t => t.Notices.Count);

		RebuildLocalizedValues(notices);
	}

	private static LegChip WalkChip(
		JourneyStrings strings,
		TimeSpan duration) =>
		new(
			string.Format(
				CultureInfo.CurrentCulture,
				"{0} {1}",
				strings.Walk,
				Format.Duration(duration)),
			ModeChips.For(TransitMode.Walk));

/// <summary>
/// The one next step (B11's simple cards): the feeder's bike ride, the walk to the first stop, or the first
/// ride's line and where it goes. Empty when the journey says nothing usable.
/// </summary>
private string NextStep(
		JourneyStrings strings)
{
		JourneyLeg? firstRide =
			Journey.Legs.FirstOrDefault(leg => leg.IsRide);

		if (Journey.Legs.Count > 0
			&& Journey.Legs[0].Mode == TransitMode.Bike)
	{
		return BikeChipText(strings, Journey.Legs[0]);
	}

		if (Journey.AccessDuration > TimeSpan.Zero
			&& firstRide is { } ride)
	{
		return $"{strings.Walk} {Format.Duration(Journey.AccessDuration)} \u00b7 {strings.To} {ride.From.Name}";
	}

		if (firstRide is { } first)
	{
		string line = first.Line?.Name ?? Format.TransportMode(first.Mode);
		string towards =
			first.Line?.Destination is { Length: > 0 } destination
				? destination
				: first.To.Name;

		return $"{line} \u00b7 {strings.To} {towards}";
	}

		return Journey.AccessDuration > TimeSpan.Zero
			? $"{strings.Walk} {Format.Duration(Journey.AccessDuration)}"
			: string.Empty;
}

/// <summary>The feeder's compact track summary: kilometres, hills, riding time.</summary>
private static string BikeChipText(
		JourneyStrings strings,
		JourneyLeg leg) =>
		string.Format(
			CultureInfo.CurrentCulture,
			strings.BikeSummary,
			(leg.DistanceMeters ?? 0) / 1000.0,
			leg.AscentMeters ?? 0,
			leg.DescentMeters ?? 0,
			Format.Duration(
				leg.EffectiveDeparture,
				leg.EffectiveArrival));

	private void RebuildLocalizedValues(int notices)
	{
		JourneyStrings strings =
			_localization.CurrentStrings.Journey;

		var chips = new List<LegChip>();

		// A walk to the first stop (or from the last one) is not a leg of its own.
		if (Journey.AccessDuration > TimeSpan.Zero)
		{
			chips.Add(WalkChip(strings, Journey.AccessDuration));
		}

		chips.AddRange(Journey.Legs
			.Select(
				leg =>
					new LegChip(
						leg.Mode == TransitMode.Walk
							? string.Format(
								CultureInfo.CurrentCulture,
								"{0} {1}",
								strings.Walk,
								Format.Duration(
									leg.EffectiveDeparture,
									leg.EffectiveArrival))
							: leg.Mode == TransitMode.Bike
									? BikeChipText(strings, leg)
									: leg.Line?.Name
								?? Format.TransportMode(leg.Mode),
						ModeChips.For(leg.Mode))));

		if (Journey.EgressDuration > TimeSpan.Zero)
		{
			chips.Add(WalkChip(strings, Journey.EgressDuration));
		}

		_chips = chips;

		_nextStepText = NextStep(strings);

		_transfersText = !Journey.Rides.Any()
			? strings.Walk
			: Journey.TransferCount switch
		{
			0 => strings.Direct,
			1 => strings.OneTransfer,
			int n => string.Format(
				CultureInfo.CurrentCulture,
				strings.MultipleTransfers,
				n)
		};

		_blockText = JourneyBlockText.Describe(_block, strings);

		_noticesText = notices switch
		{
			1 => strings.OneNotice,
			_ => string.Format(
				CultureInfo.CurrentCulture,
				strings.MultipleNotices,
				notices)
		};

		_hasNotices = notices > 0;

		_accessibilityText =
			string.Format(
				CultureInfo.CurrentCulture,
				strings.AccessibilitySummary,
				DepartureTime,
				ArrivalTime,
				DurationText,
				TransfersText)
			+ (IsCancelled
				? $", {_blockText}"
				: string.Empty)
			+ (ArrivalDelay is { } late
				? string.Format(
					CultureInfo.CurrentCulture,
					strings.AccessibilityArrivalDelay,
					late)
				: string.Empty)
			+ (HasNotices
				? string.Format(
					CultureInfo.CurrentCulture,
					strings.AccessibilityNotices,
					NoticesText)
				: string.Empty);

		OnPropertyChanged(nameof(BlockText));
		OnPropertyChanged(nameof(TransfersText));
		OnPropertyChanged(nameof(Chips));
		OnPropertyChanged(nameof(NoticesText));
		OnPropertyChanged(nameof(HasNotices));
		OnPropertyChanged(nameof(HasStatusRow));
		OnPropertyChanged(nameof(AccessibilityText));
		OnPropertyChanged(nameof(NextStepText));
		OnPropertyChanged(nameof(HasNextStep));
	}
}