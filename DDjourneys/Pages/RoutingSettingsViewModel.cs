using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Route preferences: what the planner has to respect on the next search.</summary>
public sealed partial class RoutingSettingsViewModel : DisposableViewModel
{
	private readonly AppSettings _settings;
	private readonly ProviderRegistry _providers;
	private readonly LocalizationService _localization;

	public RoutingSettingsViewModel(AppSettings settings, ProviderRegistry providers)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(providers);

		_settings = settings;
		_providers = providers;
		_localization = LocalizationService.Current;

		ListenToLocalization(_localization, OnLocalizationChanged);

		Subscribe(
			() => _providers.SelectionChanged += OnProviderChanged,
			() => _providers.SelectionChanged -= OnProviderChanged);

		Modes =
			(List<ToggleOption>)
			[
				Mode(ModeFilter.Tram, s => s.Tram),
				Mode(ModeFilter.CityBus, s => s.CityBus),
				Mode(ModeFilter.IntercityBus, s => s.IntercityBus),
				Mode(ModeFilter.SuburbanRailway, s => s.SuburbanRailway),
				Mode(ModeFilter.Train, s => s.Train),
				Mode(ModeFilter.Cableway, s => s.Cableway),
				Mode(ModeFilter.Ferry, s => s.Ferry),
				Mode(ModeFilter.HailedSharedTaxi, s => s.HailedSharedTaxi)
			];

		Transfers =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					((int)MaxTransfers.Unlimited, () => Strings.TransfersUnlimited),
					((int)MaxTransfers.Two, () => Strings.TransfersTwo),
					((int)MaxTransfers.One, () => Strings.TransfersOne),
					((int)MaxTransfers.None, () => Strings.TransfersNone)
				],
				() => (int)_settings.MaxTransfers,
				value => _settings.MaxTransfers = (MaxTransfers)value);

		ViaStay =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					(1, () => Strings.ViaAny),
					(3, () => ViaStayText(3)),
					(5, () => ViaStayText(5)),
					(10, () => ViaStayText(10)),
					(15, () => ViaStayText(15)),
					(20, () => ViaStayText(20)),
					(30, () => ViaStayText(30)),
					(45, () => ViaStayText(45)),
					(60, () => ViaStayText(60))
				],
				() => Math.Max(1, _settings.ViaMinutes),
				value => _settings.ViaMinutes = value);

		Pace =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					((int)WalkingPace.VerySlow, () => Strings.PaceVerySlow),
					((int)WalkingPace.Slow, () => Strings.PaceSlow),
					((int)WalkingPace.Normal, () => Strings.PaceNormal),
					((int)WalkingPace.Fast, () => Strings.PaceFast),
					((int)WalkingPace.VeryFast, () => Strings.PaceVeryFast)
				],
				() => (int)_settings.WalkingPace,
				value => _settings.WalkingPace = (WalkingPace)value);

		Accessibility =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					((int)AccessibilityNeed.None, () => Strings.AccessNone),
					((int)AccessibilityNeed.Medium, () => Strings.AccessMedium),
					((int)AccessibilityNeed.High, () => Strings.AccessHigh)
				],
				() => (int)_settings.Accessibility,
				value => _settings.Accessibility = (AccessibilityNeed)value);

		Optimisation =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					((int)RouteOptimisation.Fastest, () => ExtraStrings.OptFastest),
					((int)RouteOptimisation.FewestChanges, () => ExtraStrings.OptFewestChanges),
					((int)RouteOptimisation.LeastWalking, () => ExtraStrings.OptLeastWalking),
					((int)RouteOptimisation.LowestFare, () => ExtraStrings.OptLowestFare)
				],
				() => (int)_settings.Optimisation,
				value => _settings.Optimisation = (RouteOptimisation)value);

		Entrance =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					((int)EntranceNeed.Any, () => Strings.EntranceAny),
					((int)EntranceNeed.SmallStep, () => Strings.EntranceSmallStep),
					((int)EntranceNeed.NoStep, () => Strings.EntranceNoStep)
				],
				() => (int)_settings.Entrance,
				value => _settings.Entrance = (EntranceNeed)value);

		Passenger =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					((int)PassengerCategory.Adult, () => ExtraStrings.PassengerAdult),
					((int)PassengerCategory.Youth, () => ExtraStrings.PassengerYouth),
					((int)PassengerCategory.Child, () => ExtraStrings.PassengerChild),
					((int)PassengerCategory.Senior, () => ExtraStrings.PassengerSenior)
				],
				() => (int)_settings.Passenger,
				value => _settings.Passenger = (PassengerCategory)value);

		ExtraCharge =
			new ChoiceGroup(
				(List<(int Code, Func<string> Title)>)
				[
					((int)ExtraChargeFilter.Any, () => Strings.ExtraChargeAny),
					((int)ExtraChargeFilter.None, () => Strings.ExtraChargeNone),
					((int)ExtraChargeFilter.LocalTraffic, () => Strings.ExtraChargeLocal)
				],
				() => (int)_settings.ExtraCharge,
				value => _settings.ExtraCharge = (ExtraChargeFilter)value);

		Walking =
			(List<ToggleOption>)
			[
				new(
					() => Strings.AlternativeStops,
					() => Strings.AlternativeStopsDescription,
					() => _settings.AlternativeStops,
					value =>
					{
						_settings.AlternativeStops = value;
						return true;
					})
			];

		More =
			(List<ToggleOption>)
			[
				new(
					() => Strings.AvoidStairs,
					() => Strings.AvoidStairsDescription,
					() => _settings.AvoidStairs,
					value =>
					{
						_settings.AvoidStairs = value;
						return true;
					}),
				new(
					() => Strings.AvoidEscalators,
					() => Strings.AvoidEscalatorsDescription,
					() => _settings.AvoidEscalators,
					value =>
					{
						_settings.AvoidEscalators = value;
						return true;
					}),
				new(
					() => Strings.FewestTransfers,
					() => Strings.FewestTransfersDescription,
					() => _settings.FewestTransfers,
					value =>
					{
						_settings.FewestTransfers = value;
						return true;
					})
			];

		ResetCommand = new Command(Reset);
	}

	public IReadOnlyList<ToggleOption> Modes { get; }

	public ChoiceGroup Transfers { get; }

	/// <summary>How long the journey stays at the stop-over (client-side; every provider).</summary>
	public ChoiceGroup ViaStay { get; }

	/// <summary>A stay at the stop-over with its unit: a bare "3" does not say minutes.</summary>
	private string ViaStayText(int minutes) =>
		string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.ViaMinutesValue, minutes);

	public ChoiceGroup Pace { get; }

	public ChoiceGroup Accessibility { get; }

	/// <summary>What the router optimises for (providers with route optimisation).</summary>
	public ChoiceGroup Optimisation { get; }

	/// <summary>Required vehicle entrance.</summary>
	public ChoiceGroup Entrance { get; }

	/// <summary>Who the tickets are for.</summary>
	public ChoiceGroup Passenger { get; }

	/// <summary>Fare supplements to avoid.</summary>
	public ChoiceGroup ExtraCharge { get; }

	/// <summary>Switches for the walking section (below the pace list).</summary>
	public IReadOnlyList<ToggleOption> Walking { get; }

	/// <summary>Remaining switches: stairs, escalators, fewest transfers.</summary>
	public IReadOnlyList<ToggleOption> More { get; }

	public Command ResetCommand { get; }

	// ----- What the selected provider can do with a preference. A control that would change nothing is not shown. -----

	/// <summary>Walking time to a stop and nearby stops.</summary>
	public bool ShowWalkToStops => _providers.Supports(ProviderCapabilities.WalkToStops);

	/// <summary>Who the tickets are for.</summary>
	public bool ShowPassenger => _providers.Supports(ProviderCapabilities.PassengerFares);

	/// <summary>Fastest, fewest changes, least walking, lowest fare.</summary>
	public bool ShowOptimisation => _providers.Supports(ProviderCapabilities.RouteOptimisation);

	/// <summary>Journeys without fare supplements.</summary>
	public bool ShowExtraCharge => _providers.Supports(ProviderCapabilities.SupplementFilter);

	private void OnProviderChanged(object? sender, string providerId) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					OnPropertyChanged(nameof(ShowWalkToStops));
					OnPropertyChanged(nameof(ShowPassenger));
					OnPropertyChanged(nameof(ShowOptimisation));
					OnPropertyChanged(nameof(ShowExtraCharge));
					RefreshAll();
				}
			});

	public const double MaxFootpath = AppSettings.MaxFootpathMinutes;

	public double FootpathMinutes
	{
		get => _settings.FootpathMinutes;
		set
		{
			int rounded = (int)Math.Round(value);

			if (rounded != _settings.FootpathMinutes)
			{
				_settings.FootpathMinutes = rounded;
			}

			OnPropertyChanged();
			OnPropertyChanged(nameof(FootpathText));
		}
	}

	public string FootpathText =>
		string.Format(
			Strings.FootpathDescription,
			_settings.FootpathMinutes);

	private ExtrasStrings ExtraStrings =>
		_localization.CurrentStrings.Extras;

	private RoutingStrings Strings =>
		_localization.CurrentStrings.Routing;

	/// <summary>One switch per mode; the last remaining mode cannot be switched off.</summary>
	private ToggleOption Mode(
		ModeFilter mode,
		Func<RoutingStrings, string> title) =>
		new(
			() => title(Strings),
			null,
			() => _settings.Modes.HasFlag(mode),
			value =>
			{
				ModeFilter next =
					value
						? _settings.Modes | mode
						: _settings.Modes & ~mode;

				if (next == ModeFilter.None)
				{
					return false;
				}

				_settings.Modes = next;
				return true;
			});

	private void Reset()
	{
		_settings.ResetRoutingDefaults();
		RefreshAll();
	}

	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					RefreshAll();
				}
			});

	private void RefreshAll()
	{
		foreach (ToggleOption option in Modes.Concat(Walking).Concat(More))
		{
			option.Refresh();
		}

		Transfers.Refresh();
		ViaStay.Refresh();
		Pace.Refresh();
		Accessibility.Refresh();
		Entrance.Refresh();
		Optimisation.Refresh();
		ExtraCharge.Refresh();
		Passenger.Refresh();

		OnPropertyChanged(nameof(FootpathMinutes));
		OnPropertyChanged(nameof(FootpathText));
	}
}
