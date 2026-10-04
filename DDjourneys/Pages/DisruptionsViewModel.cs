using System.Collections.ObjectModel;
using System.Globalization;
using DDjourneys.Core.Models;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>A line named by a route change.</summary>
public sealed record DisruptionLineRow(
	DisruptionLine Line)
{
	public string Name =>
		Line.Name;

	public ChipLook Look =>
		ModeChips.For(Line.Mode);
}


/// <summary>A route change as the list shows it; the description opens on tap.</summary>
public sealed class DisruptionRow : ObservableObject
{
	private readonly Disruption _change;

	public DisruptionRow(Disruption change)
	{
		ArgumentNullException.ThrowIfNull(change);

		_change = change;

		Lines =
			[.. change.Lines.Select(line => new DisruptionLineRow(line))];

		ToggleCommand =
			new Command(
				() => IsExpanded = !IsExpanded);
	}

	public string Id =>
		_change.Id;

	public string Title =>
		_change.Title;

	public string Description =>
		_change.Description;

	public bool HasDescription =>
		Description.Length > 0;

	public IReadOnlyList<DisruptionLineRow> Lines { get; }

	public bool HasLines =>
		Lines.Count > 0;

	public bool AffectsRouting =>
		_change.AffectsRouting;

	public string KindText
	{
		get
		{
			DisruptionsStrings strings =
				LocalizationService.Current.CurrentStrings.Disruptions;

			return _change.IsPlanned
				? strings.Planned
				: strings.ShortTerm;
		}
	}

	public string AffectsRoutingText =>
		LocalizationService.Current.CurrentStrings.Disruptions.AffectsRouting;

	/// <summary>The first validity period, as "from – until".</summary>
	public string? PeriodText
	{
		get
		{
			DisruptionPeriod? period =
				_change.Periods.FirstOrDefault();

			if (period is null)
			{
				return null;
			}

			DisruptionsStrings strings =
				LocalizationService.Current.CurrentStrings.Disruptions;

			string? begin = Stamp(period.Begin);
			string? end = Stamp(period.End);

			return (begin, end) switch
			{
				(not null, not null) =>
					string.Format(CultureInfo.CurrentCulture, strings.Range, begin, end),
				(not null, null) =>
					string.Format(CultureInfo.CurrentCulture, strings.From, begin),
				(null, not null) =>
					string.Format(CultureInfo.CurrentCulture, strings.Until, end),
				_ => null
			};
		}
	}

	public bool HasPeriod =>
		PeriodText is not null;

	public bool IsExpanded
	{
		get => field;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(ShowDescription));
			}
		}
	}

	public bool ShowDescription =>
		IsExpanded && HasDescription;

	public Command ToggleCommand { get; }

	private static string? Stamp(DateTimeOffset? value) =>
		value is { } moment
			? moment.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
			: null;
}


/// <summary>A general notice of the network.</summary>
public sealed record BannerRow(
	NetworkBanner Banner)
{
	public string Title =>
		Banner.Title;

	public string Description =>
		Banner.Description;

	public bool HasDescription =>
		Description.Length > 0;
}


/// <summary>
/// Route changes of the network (rc): construction, detours and short-term disruptions, filterable by line
/// and, when opened from a departure, limited to the changes of that departure.
/// </summary>
public sealed class DisruptionsViewModel : DisposableViewModel, IQueryAttributable
{
	private readonly NetworkService _network;
	private readonly LocalizationService _localization;
	private IReadOnlyList<Disruption> _changes = [];
	private HashSet<string> _only = new(StringComparer.Ordinal);
	private CancellationTokenSource? _load;
	private bool _loaded;

	public DisruptionsViewModel(
		NetworkService network)
	{
		ArgumentNullException.ThrowIfNull(network);

		_network = network;
		_localization = LocalizationService.Current;

		RefreshCommand =
			new AsyncCommand(
				LoadAsync);

		ShowAllCommand =
			new Command(
				() =>
				{
					_only = new HashSet<string>(StringComparer.Ordinal);
					OnPropertyChanged(nameof(IsLimited));
					Rebuild();
				});
	}

	public AsyncCommand RefreshCommand { get; }

	public Command ShowAllCommand { get; }

	public ObservableCollection<DisruptionRow> Rows { get; } = [];

	public ObservableCollection<BannerRow> Banners { get; } = [];

	public bool ShortTermOnly
	{
		get => field;

		set
		{
			if (SetProperty(ref field, value) && _loaded)
			{
				_ = LoadAsync();
			}
		}
	}

	public string Filter
	{
		get => field;

		set
		{
			if (SetProperty(ref field, value ?? string.Empty))
			{
				Rebuild();
			}
		}
	} = string.Empty;

	public bool IsLimited =>
		_only.Count > 0;

	public bool IsBusy
	{
		get => field;
		private set => SetProperty(ref field, value);
	}

	public bool HasBanners =>
		Banners.Count > 0;

	public string Message
	{
		get => field;

		private set
		{
			if (SetProperty(ref field, value))
			{
				OnPropertyChanged(nameof(HasMessage));
			}
		}
	} = string.Empty;

	public bool HasMessage =>
		Message.Length > 0;

	public void ApplyQueryAttributes(
		IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.ChangeIds, out object? value)
			&& value is string ids)
		{
			_only =
				[.. ids.Split(
					',',
					StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

			OnPropertyChanged(nameof(IsLimited));
		}

		_ = LoadAsync();
	}

	private async Task LoadAsync()
	{
		if (IsDisposed)
		{
			return;
		}

		_load?.Cancel();

		var cts = new CancellationTokenSource();
		_load = cts;
		IsBusy = true;

		try
		{
			DisruptionReport report =
				await _network.GetDisruptionsAsync(
					ShortTermOnly,
					cts.Token);

			if (cts.IsCancellationRequested || IsDisposed)
			{
				return;
			}

			_changes = report.Changes;
			_loaded = true;

			Banners.Clear();

			foreach (NetworkBanner banner in report.Banners)
			{
				Banners.Add(new BannerRow(banner));
			}

			OnPropertyChanged(nameof(HasBanners));
			Rebuild();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Disruptions failed: {ex}");

			Message =
				string.IsNullOrWhiteSpace(ex.Message)
					? _localization.CurrentStrings.Common.SomethingWentWrong
					: ex.Message;
		}
		finally
		{
			if (ReferenceEquals(_load, cts))
			{
				IsBusy = false;
			}
		}
	}

	private void Rebuild()
	{
		string filter = Filter.Trim();

		IEnumerable<Disruption> shown = _changes;

		if (_only.Count > 0)
		{
			shown = shown.Where(change => _only.Contains(change.Id));
		}

		if (filter.Length > 0)
		{
			shown =
				shown.Where(
					change => change.Lines.Any(
						line => line.Name.Contains(
							filter,
							StringComparison.CurrentCultureIgnoreCase)));
		}

		Rows.Clear();

		foreach (Disruption change in shown)
		{
			Rows.Add(new DisruptionRow(change));
		}

		Message =
			Rows.Count == 0 && _loaded
				? _localization.CurrentStrings.Disruptions.None
				: string.Empty;
	}

	protected override void OnDisposing() =>
		_load?.Cancel();
}
