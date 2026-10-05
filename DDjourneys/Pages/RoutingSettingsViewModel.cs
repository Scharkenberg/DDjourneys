using DDjourneys.Core.Models;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>Route preferences: what the planner has to respect on the next search.</summary>
public sealed class RoutingSettingsViewModel : DisposableViewModel
{
	private readonly AppSettings _settings;
	private readonly LocalizationService _localization;

	public RoutingSettingsViewModel(AppSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		_settings = settings;
		_localization = LocalizationService.Current;

		ListenToLocalization(_localization, OnLocalizationChanged);

		Modes =
		new List<ToggleOption>
		{
			Mode(ModeFilter.Tram, s => s.Tram),
			Mode(ModeFilter.CityBus, s => s.CityBus),
			Mode(ModeFilter.IntercityBus, s => s.IntercityBus),
			Mode(ModeFilter.SuburbanRailway, s => s.SuburbanRailway),
			Mode(ModeFilter.Train, s => s.Train),
			Mode(ModeFilter.Cableway, s => s.Cableway),
			Mode(ModeFilter.Ferry, s => s.Ferry),
			Mode(ModeFilter.HailedSharedTaxi, s => s.HailedSharedTaxi)
		};

		Transfers =
			new ChoiceGroup(
				new List<(int Code, Func<string> Title)>
		{
					((int)MaxTransfers.Unlimited, () => Strings.TransfersUnlimited),
					((int)MaxTransfers.Two, () => Strings.TransfersTwo),
					((int)MaxTransfers.One, () => Strings.TransfersOne),
					((int)MaxTransfers.None, () => Strings.TransfersNone)
		},
				() => (int)_settings.MaxTransfers,
				value => _settings.MaxTransfers = (MaxTransfers)value);

		Pace =
			new ChoiceGroup(
				new List<(int Code, Func<string> Title)>
		{
					((int)WalkingPace.VerySlow, () => Strings.PaceVerySlow),
					((int)WalkingPace.Slow, () => Strings.PaceSlow),
					((int)WalkingPace.Normal, () => Strings.PaceNormal),
					((int)WalkingPace.Fast, () => Strings.PaceFast),
					((int)WalkingPace.VeryFast, () => Strings.PaceVeryFast)
		},
				() => (int)_settings.WalkingPace,
				value => _settings.WalkingPace = (WalkingPace)value);

		Accessibility =
			new ChoiceGroup(
				new List<(int Code, Func<string> Title)>
		{
					((int)AccessibilityNeed.None, () => Strings.AccessNone),
					((int)AccessibilityNeed.Medium, () => Strings.AccessMedium),
					((int)AccessibilityNeed.High, () => Strings.AccessHigh)
		},
				() => (int)_settings.Accessibility,
				value => _settings.Accessibility = (AccessibilityNeed)value);

		Optimisation =
			new ChoiceGroup(
				new List<(int Code, Func<string> Title)>
		{
					((int)RouteOptimisation.Fastest, () => ExtraStrings.OptFastest),
					((int)RouteOptimisation.FewestChanges, () => ExtraStrings.OptFewestChanges),
					((int)RouteOptimisation.LeastWalking, () => ExtraStrings.OptLeastWalking),
					((int)RouteOptimisation.LowestFare, () => ExtraStrings.OptLowestFare)
		},
				() => (int)_settings.Optimisation,
				value => _settings.Optimisation = (RouteOptimisation)value);

		Entrance =
			new ChoiceGroup(
				new List<(int Code, Func<string> Title)>
		{
					((int)EntranceNeed.Any, () => Strings.EntranceAny),
					((int)EntranceNeed.SmallStep, () => Strings.EntranceSmallStep),
					((int)EntranceNeed.NoStep, () => Strings.EntranceNoStep)
		},
				() => (int)_settings.Entrance,
				value => _settings.Entrance = (EntranceNeed)value);

		ExtraCharge =
			new ChoiceGroup(
				new List<(int Code, Func<string> Title)>
		{
					((int)ExtraChargeFilter.Any, () => Strings.ExtraChargeAny),
					((int)ExtraChargeFilter.None, () => Strings.ExtraChargeNone),
					((int)ExtraChargeFilter.LocalTraffic, () => Strings.ExtraChargeLocal)
		},
				() => (int)_settings.ExtraCharge,
				value => _settings.ExtraCharge = (ExtraChargeFilter)value);

		Walking =
		new List<ToggleOption>
		{
			new ToggleOption(
				() => Strings.AlternativeStops,
				() => Strings.AlternativeStopsDescription,
				() => _settings.AlternativeStops,
				value =>
				{
					_settings.AlternativeStops = value;
					return true;
				})
		};

		More =
		new List<ToggleOption>
		{
			new ToggleOption(
				() => Strings.AvoidStairs,
				() => Strings.AvoidStairsDescription,
				() => _settings.AvoidStairs,
				value =>
				{
					_settings.AvoidStairs = value;
					return true;
				}),
			new ToggleOption(
				() => Strings.AvoidEscalators,
				() => Strings.AvoidEscalatorsDescription,
				() => _settings.AvoidEscalators,
				value =>
				{
					_settings.AvoidEscalators = value;
					return true;
				}),
			new ToggleOption(
				() => Strings.FewestTransfers,
				() => Strings.FewestTransfersDescription,
				() => _settings.FewestTransfers,
				value =>
				{
					_settings.FewestTransfers = value;
					return true;
				})
		};

		ResetCommand = new Command(Reset);
	}

	public IReadOnlyList<ToggleOption> Modes { get; }

	public ChoiceGroup Transfers { get; }

	public ChoiceGroup Pace { get; }

	public ChoiceGroup Accessibility { get; }

	/// <summary>What the router optimises for (providers with route optimisation).</summary>
	public ChoiceGroup Optimisation { get; }

	/// <summary>Required vehicle entrance.</summary>
	public ChoiceGroup Entrance { get; }

	/// <summary>Fare supplements to avoid.</summary>
	public ChoiceGroup ExtraCharge { get; }

	/// <summary>Switches for the walking section (below the pace list).</summary>
	public IReadOnlyList<ToggleOption> Walking { get; }

	/// <summary>Remaining switches: stairs, escalators, fewest transfers.</summary>
	public IReadOnlyList<ToggleOption> More { get; }

	public Command ResetCommand { get; }

	public double MaxFootpath => AppSettings.MaxFootpathMinutes;

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
		Pace.Refresh();
		Accessibility.Refresh();
		Entrance.Refresh();
		Optimisation.Refresh();
		ExtraCharge.Refresh();

		OnPropertyChanged(nameof(FootpathMinutes));
		OnPropertyChanged(nameof(FootpathText));
	}
}
