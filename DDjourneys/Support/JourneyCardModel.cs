using DDjourneys.Core.Models;
using DDjourneys.Localization;

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
			TransitMode.Walk => "ModeWalk",
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
public sealed class JourneyCardModel : ObservableObject
{
	private readonly LocalizationService _localization;

	private string _transfersText = string.Empty;
	private IReadOnlyList<LegChip> _chips = [];
	private string _noticesText = string.Empty;
	private bool _hasNotices;
	private string _accessibilityText = string.Empty;

	/// <summary>
	/// Set once the entrance animation ran, so recycled cards never replay it while scrolling.
	/// </summary>
	public bool Revealed { get; set; }

	public JourneyCardModel(Journey journey)
	{
		ArgumentNullException.ThrowIfNull(journey);

		Journey = journey;
		_localization = LocalizationService.Current;

		JourneyLeg? firstRide =
			journey.Legs.FirstOrDefault(
				l => l.Mode != TransitMode.Walk);

		JourneyLeg? lastRide =
			journey.Legs.LastOrDefault(
				l => l.Mode != TransitMode.Walk);

		DepartureTime = Format.TimeOrDash(journey.Departure);
		ArrivalTime = Format.TimeOrDash(journey.Arrival);
		DurationText = Format.Duration(journey.Duration);

		// Delays come from the rides: walking legs carry no realtime data.
		DepartureDelay = Format.Delay(firstRide?.DepartureDelay);
		ArrivalDelay = Format.Delay(lastRide?.ArrivalDelay);

		IsCancelled = journey.IsCancelled;
		IsOnTime =
			!journey.IsCancelled
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

		RebuildLocalizedValues(notices);
	}

	public Journey Journey { get; }

	public string DepartureTime { get; }
	public string ArrivalTime { get; }
	public string DurationText { get; }

	public string TransfersText => _transfersText;

	public string? DepartureDelay { get; }
	public string? ArrivalDelay { get; }

	public bool HasDepartureDelay =>
		DepartureDelay is not null;

	public bool HasArrivalDelay =>
		ArrivalDelay is not null;

	public IReadOnlyList<LegChip> Chips => _chips;

	public bool IsCancelled { get; }
	public bool IsOnTime { get; }

	public string NoticesText => _noticesText;
	public bool HasNotices { get; }
	public bool HasStatusRow =>
		IsCancelled || IsOnTime || HasNotices;

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

	private void RebuildLocalizedValues(int notices)
	{
		JourneyStrings strings = _localization.CurrentStrings.Journey;

		_transfersText = Journey.TransferCount switch
		{
			0 => strings.Direct,
			1 => strings.OneTransfer,
			int n => string.Format(
				System.Globalization.CultureInfo.CurrentCulture,
				strings.MultipleTransfers,
				n)
		};

		_chips = Journey.Legs
			.Select(leg => new LegChip(
				leg.Mode == TransitMode.Walk
					? string.Format(
						System.Globalization.CultureInfo.CurrentCulture,
						"{0} {1}",
						strings.Walk,
						Format.Duration(
							leg.EffectiveDeparture,
							leg.EffectiveArrival))
					: leg.Line?.Name ?? leg.Mode.ToString(),
				ModeColors.For(leg.Mode)))
			.ToList();

		_noticesText = notices switch
		{
			1 => strings.OneNotice,
			_ => string.Format(
				System.Globalization.CultureInfo.CurrentCulture,
				strings.MultipleNotices,
				notices)
		};

		_hasNotices = notices > 0;

		_accessibilityText =
			string.Format(
				System.Globalization.CultureInfo.CurrentCulture,
				strings.AccessibilitySummary,
				DepartureTime,
				ArrivalTime,
				DurationText,
				TransfersText)
			+ (IsCancelled
				? strings.AccessibilityCancelled
				: string.Empty)
			+ (ArrivalDelay is { } late
				? string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					strings.AccessibilityArrivalDelay,
					late)
				: string.Empty)
			+ (HasNotices
				? string.Format(
					System.Globalization.CultureInfo.CurrentCulture,
					strings.AccessibilityNotices,
					NoticesText)
				: string.Empty);

		OnPropertyChanged(nameof(TransfersText));
		OnPropertyChanged(nameof(Chips));
		OnPropertyChanged(nameof(NoticesText));
		OnPropertyChanged(nameof(HasNotices));
		OnPropertyChanged(nameof(HasStatusRow));
		OnPropertyChanged(nameof(AccessibilityText));
	}
}