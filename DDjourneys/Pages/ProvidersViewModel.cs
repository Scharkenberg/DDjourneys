using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Abstractions;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>One provider as the list shows it.</summary>
public sealed class ProviderRow : ObservableObject
{
	public ProviderRow(
		ProviderInfo info,
		IReadOnlyList<string> capabilities,
		Command select)
	{
		Info = info;
		Capabilities = capabilities;
		SelectCommand = select;
	}

	public ProviderInfo Info { get; }

	public string Name => Info.Name;

	public bool IsExperimental => Info.IsExperimental;

	public string ExperimentalText =>
		LocalizationService.Current.CurrentStrings.Provider.Experimental;

	public string FullName => Info.FullName;

	public string Coverage => Info.Coverage;

	/// <summary>Localized labels of what the provider supports.</summary>
	public IReadOnlyList<string> Capabilities { get; }

	public bool IsSelected
	{
		get => field;
		internal set =>
			SetProperty(
				ref field,
				value);
	}

	public Command SelectCommand { get; }

	public string Description =>
		Info.IsExperimental
			? $"{Info.Name}, {ExperimentalText}, {Info.FullName}, {Info.Coverage}"
			: $"{Info.Name}, {Info.FullName}, {Info.Coverage}";
}


/// <summary>Providers of one region (one heading, one card).</summary>
public sealed record ProviderGroup(
	string Region,
	IReadOnlyList<ProviderRow> Rows);


/// <summary>Provider picker. Lists whatever the registry holds, grouped by region, so new providers need no UI work.</summary>
public sealed class ProvidersViewModel : DisposableViewModel
{
	private static readonly (ProviderCapabilities Flag, Func<IUiStrings, string> Label)[] Labels =
	[
		(ProviderCapabilities.Journeys, s => s.Provider.CapJourneys),
		(ProviderCapabilities.Places, s => s.Provider.CapPlaces),
		(ProviderCapabilities.Continuation, s => s.Provider.CapContinuation),
		(ProviderCapabilities.RoutingPreferences, s => s.Provider.CapRouting),
		(ProviderCapabilities.Platforms, s => s.Provider.CapPlatforms),
		(ProviderCapabilities.Occupancy, s => s.Provider.CapOccupancy),
		(ProviderCapabilities.Tracking, s => s.Provider.CapTracking),
		(ProviderCapabilities.Departures, s => s.Provider.CapDepartures),
		(ProviderCapabilities.Disruptions, s => s.Provider.CapDisruptions),
		(ProviderCapabilities.NetworkInfo, s => s.Provider.CapNetwork),
		(ProviderCapabilities.JourneyExtras, s => s.Provider.CapExtras),
		(ProviderCapabilities.LiveVehicles, s => s.Extras.CapLive),
		(ProviderCapabilities.OpenData, s => s.Extras.CapOpenData),
		(ProviderCapabilities.Fares, s => s.Extras.CapFares),
		(ProviderCapabilities.RouteOptimisation, s => s.Extras.CapOptimisation)
	];

	private readonly ProviderRegistry _registry;
	private readonly LocalizationService _localization;

	public ProvidersViewModel(ProviderRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(registry);

		_registry = registry;
		_localization = LocalizationService.Current;

		ListenToLocalization(_localization, OnLocalizationChanged);

		Build();
	}

	public IReadOnlyList<ProviderGroup> Groups
	{
		get => field;
		private set =>
			SetProperty(
				ref field,
				value);
	} = [];

	private void Select(ProviderRow row)
	{
		_registry.Select(row.Info.Id);
		RefreshSelection();
	}

	private void Build()
	{
		IUiStrings strings =
			_localization.CurrentStrings;

		Groups =
			(_registry.Providers
				.GroupBy(provider => provider.Region)
				.Select(
					group => new ProviderGroup(
						group.Key,
						(group.Select(
							provider =>
							{
								ProviderRow? row = null;

								row =
									new ProviderRow(
										provider,
										(Labels
											.Where(label => provider.Supports(label.Flag))
											.Select(label => label.Label(strings))).ToList(),
										new Command(() => Select(row!)));

								return row;
							})).ToList()))).ToList();

		RefreshSelection();
	}

	private void RefreshSelection()
	{
		foreach (ProviderRow row in Groups.SelectMany(group => group.Rows))
		{
			row.IsSelected =
				string.Equals(
					row.Info.Id,
					_registry.SelectedId,
					StringComparison.OrdinalIgnoreCase);
		}
	}

	private void OnLocalizationChanged(
		object? sender,
		System.ComponentModel.PropertyChangedEventArgs e) =>
		MainThread.BeginInvokeOnMainThread(
			() =>
			{
				if (!IsDisposed)
				{
					Build();
				}
			});
}
