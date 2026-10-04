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
	private CancellationTokenSource? _build;
	private bool _loaded;

	// Rows reach the screen in small batches: building dozens of cards in one go freezes the page.
	private const int BatchSize = 10;

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
					_ = RebuildAsync();
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
				_ = RebuildAsync(true);
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
		// Opened from a departure: the disruptions of its line (shown in the filter, so it can be cleared).
		if (query.TryGetValue(Routes.LineName, out object? line)
			&& line is string lineName
			&& lineName.Length > 0)
		{
			Filter = lineName;
		}

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
			_ = RebuildAsync();
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

	/// <summary>
	/// Line filter: names separated by comma, semicolon or space. A name matches a line exactly ("7" is line 7, not
	/// 17 or S7); letters alone match every line with that prefix and a number ("S" is S1, S2, ...).
	/// </summary>
	private static bool MatchesLines(
		Disruption change,
		string[] names) =>
		change.Lines.Any(
			line => names.Any(
				name => string.Equals(line.Name, name, StringComparison.CurrentCultureIgnoreCase)
					|| (name.All(char.IsLetter)
						&& line.Name.Length > name.Length
						&& line.Name.StartsWith(name, StringComparison.CurrentCultureIgnoreCase)
						&& line.Name[name.Length..].All(char.IsDigit))));

	private async Task RebuildAsync(
		bool debounce = false)
	{
		_build?.Cancel();

		var cts = new CancellationTokenSource();
		_build = cts;

		try
		{
			if (debounce)
			{
				await Task.Delay(250, cts.Token);
			}

			string[] names =
				Filter.Split(
					[',', ';', ' '],
					StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			IEnumerable<Disruption> source = _changes;

			if (_only.Count > 0)
			{
				source = source.Where(change => _only.Contains(change.Id));
			}

			if (names.Length > 0)
			{
				source = source.Where(change => MatchesLines(change, names));
			}

			Disruption[] shown = [.. source];

			Rows.Clear();

			for (int i = 0; i < shown.Length; i += BatchSize)
			{
				cts.Token.ThrowIfCancellationRequested();

				foreach (Disruption change in shown.Skip(i).Take(BatchSize))
				{
					Rows.Add(new DisruptionRow(change));
				}

				if (i + BatchSize < shown.Length)
				{
					await Task.Delay(16, cts.Token);
				}
			}

			Message =
				shown.Length == 0 && _loaded
					? _localization.CurrentStrings.Disruptions.None
					: string.Empty;
		}
		catch (OperationCanceledException)
		{
		}
	}

	protected override void OnDisposing()
	{
		_load?.Cancel();
		_build?.Cancel();
	}
}
