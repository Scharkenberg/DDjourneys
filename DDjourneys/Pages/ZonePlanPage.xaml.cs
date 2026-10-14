using System.Globalization;
using DDjourneys.Controls;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Models;
using DDjourneys.Core.Providers;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Core.Services;
using DDjourneys.Localization;
using DDjourneys.Support;
using Location = DDjourneys.Core.Models.Location;

namespace DDjourneys.Pages;

/// <summary>
/// All the plans of the area in one page: the fare zone plan of the whole area and the Dresden network plan (from
/// the tiles and stop list the VVO website uses, see <see cref="ZonePlanStore"/>; offline once seen), and the DVB's
/// night, barrier-free and MOBI city plans (PDF files drawn as tiles, see <see cref="NetworkMapStore"/>). A tap on
/// a stop of the first two offers its departures and journeys to or from it; the search finds a stop by name,
/// "locate" marks the stop nearest to the passenger, and a journey opens the plan with both ends marked and in
/// view (<see cref="Routes.ZonePlanTarget"/>) - the zones it crosses are what the ticket costs. A document, not a
/// pane page: a plain Shell navigation, popped by back.
/// </summary>
public sealed partial class ZonePlanPage : ContentPage, IQueryAttributable
{
	private const string KindPreference = "ZonePlanKind";

	/// <summary>From this scale on (CSS px per plan pixel) the stops are told apart and can be tapped.</summary>
	private const double StopScale = 0.5;

	private const int MaxMatches = 8;

	private static readonly TimeSpan ToastTime = TimeSpan.FromSeconds(4);

	private static readonly TimeSpan WatchPlan = TimeSpan.FromSeconds(30);

	/// <summary>The plans the page offers; the first two have stops, the others are DVB documents.</summary>
	private enum Choice
	{
		Region,
		Dresden,
		Night,
		BarrierFree,
		City
	}

	private readonly ZonePlanStore _store;
	private readonly NetworkMapStore _maps;
	private readonly PlannerLauncher _planner;
	private readonly LocationService _locations;
	private readonly ProviderRegistry _providers;

	private Choice _choice;
	private bool _loaded;
	private ZonePlan? _plan;
	private Dictionary<string, VvoPlanPin> _pins = [];
	private ILookup<string, VvoPlanPin> _byName = Array.Empty<VvoPlanPin>().ToLookup(pin => pin.Id);
	private CancellationTokenSource? _load;
	private CancellationTokenSource? _toast;
	private ZonePlanTarget? _target;
	private Location? _here;
	private bool _busy;

	// A document plan on its way in: until the view answers (shown or failed), a watchdog makes sure the page never
	// stays a silent white sheet - the JPG stands in first, the message with a retry after that.
	private CancellationTokenSource? _await;
	private bool _awaiting;
	private bool _fellBack;

	public ZonePlanPage(ZonePlanStore store, NetworkMapStore maps, PlannerLauncher planner, LocationService locations, ProviderRegistry providers)
	{
		InitializeComponent();

		_store = store;
		_maps = maps;
		_planner = planner;
		_locations = locations;
		_providers = providers;
		_choice = Enum.TryParse(Preferences.Default.Get(KindPreference, nameof(Choice.Dresden)), out Choice saved) ? saved : Choice.Dresden;

		ShowKind();
	}

	private static ExtrasStrings Strings => LocalizationService.Current.CurrentStrings.Extras;

	private bool HasStops => _choice <= Choice.Dresden;

	private VvoZonePlanKind ZoneKind => _choice == Choice.Region ? VvoZonePlanKind.Region : VvoZonePlanKind.Dresden;

	private VvoNetworkMapKind DocumentKind =>
		_choice switch
		{
			Choice.Night => VvoNetworkMapKind.Night,
			Choice.BarrierFree => VvoNetworkMapKind.BarrierFree,
			_ => VvoNetworkMapKind.CityMap
		};

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		if (query.TryGetValue(Routes.ZonePlanTarget, out object? value)
			&& value is ZonePlanTarget target)
		{
			// The zones a journey crosses are on the plan of the area; Dresden is one zone of it.
			_target = target;
			_choice = Choice.Region;
			_loaded = false;
			ShowKind();
		}
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		if (!_loaded)
		{
			Load(refresh: false);
		}
	}

	protected override void OnDisappearing()
	{
		_load?.Cancel();
		_toast?.Cancel();

		base.OnDisappearing();
	}

	// ---- plan ----

	private void RegionTapped(object? sender, TappedEventArgs e) =>
		Switch(Choice.Region);

	private void DresdenTapped(object? sender, TappedEventArgs e) =>
		Switch(Choice.Dresden);

	private void NightTapped(object? sender, TappedEventArgs e) =>
		Switch(Choice.Night);

	private void BarrierFreeTapped(object? sender, TappedEventArgs e) =>
		Switch(Choice.BarrierFree);

	private void CityTapped(object? sender, TappedEventArgs e) =>
		Switch(Choice.City);

	private void RetryClicked(object? sender, EventArgs e) =>
		Load(refresh: false);

	private void RefreshClicked(object? sender, EventArgs e) =>
		Load(refresh: true);

	/// <summary>The view was rebuilt (its web view torn down and re-created): the plan went with the old instance.</summary>
	private void ReloadPlan(object? sender, EventArgs e)
	{
		_loaded = false;

		Load(refresh: false);
	}

	private void Switch(Choice choice)
	{
		if (choice == _choice && _loaded)
		{
			return;
		}

		_choice = choice;
		Preferences.Default.Set(KindPreference, choice.ToString());
		ShowKind();
		Load(refresh: false);
	}

	private void ShowKind()
	{
		Mark(SegRegion, SegRegionLabel, _choice == Choice.Region);
		Mark(SegDresden, SegDresdenLabel, _choice == Choice.Dresden);
		Mark(SegNight, SegNightLabel, _choice == Choice.Night);
		Mark(SegBarrierFree, SegBarrierFreeLabel, _choice == Choice.BarrierFree);
		Mark(SegCity, SegCityLabel, _choice == Choice.City);

		SearchButton.IsVisible = HasStops;
		LocateButton.IsVisible = HasStops;
		PdfButton.IsVisible = !HasStops;
		Credit.Text = HasStops ? Strings.ZonePlanCredit : Strings.NetworkMapCredit;
		MessageText.Text = HasStops ? Strings.ZonePlanFailed : Strings.NetworkMapFailed;
	}

	private static void Mark(Border chip, Label text, bool on)
	{
		Themed.SetIsOn(chip, on);
		Themed.SetIsOn(text, on);
	}

	private async void Load(bool refresh)
	{
		_load?.Cancel();
		_await?.Cancel();

		var cancel = new CancellationTokenSource();

		_load = cancel;
		_loaded = true;
		_awaiting = false;
		_fellBack = false;

		Message.IsVisible = false;
		Stale.IsVisible = false;
		Busy.IsRunning = true;
		Busy.IsVisible = true;
		SearchButton.IsEnabled = false;
		LocateButton.IsEnabled = false;
		RefreshButton.IsEnabled = false;
		CloseSearch();

		Choice choice = _choice;

		try
		{
			if (HasStops)
			{
				ZonePlan plan = await _store.OpenAsync(ZoneKind, cancel.Token);

				if (cancel.IsCancellationRequested)
				{
					plan.Tiles.Dispose();

					return;
				}

				_plan = plan;
				_pins = plan.Pins.GroupBy(pin => pin.Id).ToDictionary(group => group.Key, group => group.First());
				_byName = plan.Pins.ToLookup(pin => VvoZonePlans.Fold(pin.Name));

				Stale.IsVisible = !plan.Fresh;

				await Plan.ShowAsync(plan.Tiles);
				await Plan.SetStopsAsync((PlanStop[])[.. plan.Pins.Select(pin => new PlanStop(pin.Id, pin.X, pin.Y, pin.Title))], StopScale);
				await PlaceMarksAsync();

				SearchButton.IsEnabled = true;
				LocateButton.IsEnabled = true;
			}
			else
			{
				_plan = null;
				_pins = [];
				_byName = Array.Empty<VvoPlanPin>().ToLookup(pin => pin.Id);

				(IPlanTiles tiles, bool fresh) = await OpenDocumentAsync(refresh, cancel.Token);

				if (cancel.IsCancellationRequested)
				{
					tiles.Dispose();

					return;
				}

				Stale.IsVisible = !fresh;
				await ShowDocumentAsync(tiles);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Zone plan] {choice} load failed: {ex.GetType().Name}: {ex.Message}");

			Busy.IsRunning = false;
			Busy.IsVisible = false;
			Message.IsVisible = true;
			_loaded = false;
		}
		finally
		{
			if (ReferenceEquals(_load, cancel))
			{
				RefreshButton.IsEnabled = true;
			}
		}
	}

	/// <summary>
	/// The marks that outlive a switch of plans: a journey's ends (in view, both at once) and the passenger's stop,
	/// found again on the plan now shown. A journey end missing on the Dresden plan moves to the plan of the area.
	/// </summary>
	private async Task PlaceMarksAsync()
	{
		if (_target is { } target)
		{
			VvoPlanPin? from = target.From is { } a ? Find(a.Id, a.Name, a.Place) : null;
			VvoPlanPin? to = target.To is { } b ? Find(b.Id, b.Name, b.Place) : null;

			await Plan.MarkAsync(PlanMark.From, from?.Id, from?.Title);
			await Plan.MarkAsync(PlanMark.To, to?.Id, to?.Title, PlanFit.All);
		}

		if (_here is { } here
			&& Find(here.Id, here.Name, here.Place) is { } stop)
		{
			await Plan.MarkAsync(PlanMark.Here, stop.Id, stop.Title, _target is null ? PlanFit.Center : PlanFit.None);
		}
	}

	/// <summary>The plan's own title, the chip row's word for it (the viewer request carries it).</summary>
	private string DocumentTitle =>
		_choice switch
		{
			Choice.Night => Strings.NetworkMapPlanNight,
			Choice.BarrierFree => Strings.NetworkMapPlanBarrierFree,
			_ => Strings.NetworkMapPlanCityMap
		};

	/// <summary>The PDF, drawn from its vectors; the JPG at full size when the PDF cannot be had or read.</summary>
	private async Task<(IPlanTiles Tiles, bool Fresh)> OpenDocumentAsync(bool refresh, CancellationToken cancellationToken)
	{
		VvoNetworkMapKind kind = DocumentKind;
		string plan = VvoNetworkMap.PlanName(kind);

		try
		{
			NetworkMapFile pdf = refresh
				? await _maps.RefreshPdfAsync(kind, cancellationToken)
				: await _maps.GetPdfAsync(kind, cancellationToken);

			DiagnosticLog.Write($"[Network map] {plan}: PDF {new FileInfo(pdf.Path).Length / 1024} KB, fresh {pdf.Fresh}");

			return (await PdfPlanTiles.OpenAsync(pdf.Path, plan, cancellationToken), pdf.Fresh);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Network map] {plan}: PDF not shown, trying the JPG: {ex.GetType().Name}: {ex.Message}");
		}

		NetworkMapFile jpg = await _maps.GetJpgAsync(kind, refresh, cancellationToken);

		_fellBack = true;

		return (await ImagePlanTiles.OpenAsync(jpg.Path, plan, cancellationToken), jpg.Fresh);
	}

	/// <summary>Shows a document plan and keeps watch: a view that says nothing for half a minute counts as failed.</summary>
	private async Task ShowDocumentAsync(IPlanTiles tiles)
	{
		_awaiting = true;
		_fellBack = _fellBack || tiles is ImagePlanTiles;

		_await?.Cancel();

		var watch = new CancellationTokenSource();

		_await = watch;

		await Plan.ShowAsync(tiles);

		_ = WatchAsync(watch);
	}

	private async Task WatchAsync(CancellationTokenSource watch)
	{
		try
		{
			await Task.Delay(WatchPlan, watch.Token);
		}
		catch (TaskCanceledException)
		{
			return;
		}

		if (!_awaiting || !ReferenceEquals(_await, watch))
		{
			return;
		}

		DiagnosticLog.Write("[Network map] the view stayed silent; showing the JPG instead");

		await FallBackAsync();
	}

	/// <summary>The plan at full size as a picture, when the vector plan did not draw; the message after that.</summary>
	private async Task FallBackAsync()
	{
		if (_fellBack || HasStops)
		{
			GiveUp();

			return;
		}

		_fellBack = true;

		try
		{
			NetworkMapFile jpg = await _maps.GetJpgAsync(DocumentKind);
			ImagePlanTiles tiles = await ImagePlanTiles.OpenAsync(jpg.Path, VvoNetworkMap.PlanName(DocumentKind), CancellationToken.None);

			await ShowDocumentAsync(tiles);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Network map] the JPG did not show either: {ex.Message}");

			GiveUp();
		}
	}

	private void GiveUp()
	{
		_awaiting = false;
		_await?.Cancel();
		_loaded = false;
		Busy.IsRunning = false;
		Busy.IsVisible = false;
		Message.IsVisible = true;
		Plan.Opacity = 0;
	}

	private async void OpenPdfClicked(object? sender, EventArgs e)
	{
		PdfButton.IsEnabled = false;

		try
		{
			NetworkMapFile pdf = await _maps.GetPdfAsync(DocumentKind);

			await Launcher.Default.OpenAsync(new OpenFileRequest(DocumentTitle, new ReadOnlyFile(pdf.Path, "application/pdf")));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Network map] opening the PDF failed: {ex.Message}");
			ShowToast(Strings.NetworkMapFailed);
		}
		finally
		{
			PdfButton.IsEnabled = true;
		}
	}

	private async void PlanShown(object? sender, bool shown)
	{
		_awaiting = false;
		_await?.Cancel();

		Busy.IsRunning = false;
		Busy.IsVisible = false;

		if (!shown)
		{
			DiagnosticLog.Write($"[Network map] {_choice}: the plan could not be drawn");

			// The vector PDF did not draw: the JPG is a coarser plan than none at all.
			if (!HasStops && !_fellBack)
			{
				await FallBackAsync();

				return;
			}

			_loaded = false;
			Message.IsVisible = true;
			Plan.Opacity = 0;

			return;
		}

		_plan?.Tiles.Prefetch();

		if (Motion.Enabled)
		{
			await Plan.FadeToAsync(1, 220, Easing.CubicOut);
		}
		else
		{
			Plan.Opacity = 1;
		}
	}

	private void PlanZoomChanged(object? sender, PlanZoom zoom)
	{
		ZoomInButton.IsEnabled = zoom.CanZoomIn;
		ZoomOutButton.IsEnabled = zoom.CanZoomOut;
	}

	private void ZoomInClicked(object? sender, EventArgs e) =>
		_ = Plan.ZoomAsync(1);

	private void ZoomOutClicked(object? sender, EventArgs e) =>
		_ = Plan.ZoomAsync(-1);

	// ---- a stop ----

	private async void PlanStopTapped(object? sender, string id)
	{
		if (_busy
			|| !_pins.TryGetValue(id, out VvoPlanPin? pin))
		{
			return;
		}

		_busy = true;

		try
		{
			StopAction action = await StopActions.AskAsync(this, pin.Title);

			if (action == StopAction.None)
			{
				return;
			}

			if (await LocationOfAsync(pin) is not { } stop)
			{
				ShowToast(Strings.ZonePlanNoMatch);

				return;
			}

			await StopActions.RunAsync(action, stop, this, _planner);
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Zone plan] stop tap failed: {ex.Message}");
		}
		finally
		{
			_busy = false;
		}
	}

	/// <summary>
	/// The pin as a stop of the selected provider: the VVO takes the pin's own id; another provider (TRIAS) is asked
	/// for the name and the answer with the same stop number (or the same name) is taken.
	/// </summary>
	private async Task<Location?> LocationOfAsync(VvoPlanPin pin)
	{
		string place = pin.Place ?? "Dresden";

		if (string.Equals(_providers.SelectedId, VvoProviderInfo.Id, StringComparison.Ordinal))
		{
			return new Location
			{
				Id = pin.Id,
				ProviderId = VvoProviderInfo.Id,
				Name = pin.Name,
				Place = place,
				Kind = PlaceKind.Stop
			};
		}

		IReadOnlyList<Location> found = await _locations.SearchAsync($"{pin.Name} {place}", TimeSpan.FromSeconds(8));
		List<Location> stops = [.. found.Where(location => location.IsStation)];

		return stops.FirstOrDefault(stop => VvoZonePlans.VvoIdOf(stop.Id) == pin.Id)
			?? stops.FirstOrDefault(stop => SameName(stop.Name, stop.Place, pin))
			?? stops.FirstOrDefault();
	}

	/// <summary>
	/// The pin of a stop known elsewhere: by its stop number when the names agree, else by name (and place where
	/// several stops share the name).
	/// </summary>
	private VvoPlanPin? Find(string? id, string name, string? place)
	{
		if (VvoZonePlans.VvoIdOf(id) is { } vvoId
			&& _pins.TryGetValue(vvoId, out VvoPlanPin? byId)
			&& SameName(name, place, byId))
		{
			return byId;
		}

		string bare = Bare(name, place);
		List<VvoPlanPin> named = [.. _byName[VvoZonePlans.Fold(bare)]];

		if (named.Count <= 1)
		{
			return named.FirstOrDefault()
				?? _pins.Values.FirstOrDefault(pin => VvoZonePlans.Fold($"{pin.Place ?? "Dresden"} {pin.Name}") == VvoZonePlans.Fold(name));
		}

		string wantedPlace = VvoZonePlans.Fold(place ?? "Dresden");

		return named.FirstOrDefault(pin => VvoZonePlans.Fold(pin.Place ?? "Dresden") == wantedPlace) ?? named[0];
	}

	private static bool SameName(string name, string? place, VvoPlanPin pin)
	{
		string wanted = VvoZonePlans.Fold(Bare(name, place));
		string have = VvoZonePlans.Fold(pin.Name);

		return wanted.Length > 0
			&& have.Length > 0
			&& (wanted.Contains(have, StringComparison.Ordinal) || have.Contains(wanted, StringComparison.Ordinal));
	}

	/// <summary>"Dresden Postplatz" with place "Dresden" is "Postplatz".</summary>
	private static string Bare(string name, string? place) =>
		!string.IsNullOrWhiteSpace(place)
			&& name.StartsWith(place + " ", StringComparison.OrdinalIgnoreCase)
				? name[(place.Length + 1)..]
				: name;

	// ---- locate ----

	private async void LocateClicked(object? sender, EventArgs e)
	{
		LocateButton.IsEnabled = false;
		ShowToast(Strings.ZonePlanLocating, sticky: true);

		try
		{
			Location? here = await _planner.NearMeAsync();

			if (here is null)
			{
				ShowToast(Strings.ZonePlanNotNear);

				return;
			}

			_here = here;

			if (Find(here.Id, here.Name, here.Place) is { } stop)
			{
				HideToast();
				await Plan.MarkAsync(PlanMark.Here, stop.Id, stop.Title, PlanFit.Center);
			}
			else if (_choice == Choice.Dresden)
			{
				// Outside the city: the plan of the area has it, if anything does.
				ShowToast(string.Format(CultureInfo.CurrentCulture, Strings.ZonePlanSwitched, here.Name));
				Switch(Choice.Region);
			}
			else
			{
				ShowToast(Strings.ZonePlanNotNear);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.Write($"[Zone plan] locate failed: {ex.Message}");
			ShowToast(Strings.ZonePlanNotNear);
		}
		finally
		{
			LocateButton.IsEnabled = _plan is not null;
		}
	}

	// ---- search ----

	private void SearchClicked(object? sender, EventArgs e)
	{
		if (SearchPanel.IsVisible)
		{
			CloseSearch();

			return;
		}

		SearchPanel.IsVisible = true;
		SearchEntry.Focus();
	}

	private void SearchCloseClicked(object? sender, EventArgs e) =>
		CloseSearch();

	private void CloseSearch()
	{
		SearchPanel.IsVisible = false;
		Matches.IsVisible = false;
		SearchEntry.Unfocus();
		SearchEntry.Text = string.Empty;
	}

	private void SearchTextChanged(object? sender, TextChangedEventArgs e) =>
		ShowMatches(e.NewTextValue);

	private void SearchCompleted(object? sender, EventArgs e)
	{
		if (Search(SearchEntry.Text).FirstOrDefault() is { } best)
		{
			_ = PickAsync(best);
		}
	}

	/// <summary>
	/// Stops whose name starts with the words typed come first, then those with a word starting so, then any that
	/// contain them; the place counts too ("Riesa Bahnhof"). Folded, so "strasse", "str." and umlauts all meet.
	/// </summary>
	private List<VvoPlanPin> Search(string? text)
	{
		string query = VvoZonePlans.Fold(text);

		if (query.Length < 2)
		{
			return [];
		}

		string[] words = (text ?? string.Empty)
			.Split([' ', ',', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
			.Select(VvoZonePlans.Fold)
			.Where(word => word.Length > 0)
			.ToArray();

		static int Rank(VvoPlanPin pin, string query, string[] words)
		{
			string name = VvoZonePlans.Fold(pin.Name);
			string all = VvoZonePlans.Fold($"{pin.Place} {pin.Name}");
			string[] parts = $"{pin.Place} {pin.Name}"
				.Split([' ', ',', '-', '/', '.'], StringSplitOptions.RemoveEmptyEntries)
				.Select(VvoZonePlans.Fold)
				.ToArray();

			if (name.StartsWith(query, StringComparison.Ordinal))
			{
				return 0;
			}

			if (words.All(word => parts.Any(part => part.StartsWith(word, StringComparison.Ordinal))))
			{
				return 1;
			}

			return all.Contains(query, StringComparison.Ordinal) ? 2 : int.MaxValue;
		}

		return [.. _pins.Values
			.Select(pin => (Pin: pin, Rank: Rank(pin, query, words)))
			.Where(match => match.Rank != int.MaxValue)
			.OrderBy(match => match.Rank)
			.ThenBy(match => match.Pin.Name.Length)
			.ThenBy(match => match.Pin.Title, StringComparer.CurrentCulture)
			.Take(MaxMatches)
			.Select(match => match.Pin)];
	}

	private void ShowMatches(string? text)
	{
		MatchList.Children.Clear();

		if (VvoZonePlans.Fold(text).Length < 2)
		{
			Matches.IsVisible = false;

			return;
		}

		List<VvoPlanPin> matches = Search(text);

		if (matches.Count == 0)
		{
			MatchList.Children.Add(new Label { Text = Strings.ZonePlanNoMatch, StyleClass = ["Caption"], Margin = new Thickness(12, 10) });
		}

		foreach (VvoPlanPin pin in matches)
		{
			var name = new Label { Text = pin.Name, LineBreakMode = LineBreakMode.TailTruncation };

			name.SetDynamicResource(Label.FontFamilyProperty, "FontSemibold");

			var row =
				new VerticalStackLayout
				{
					Padding = new Thickness(12, 8),
					Spacing = 0,
					Children =
					{
						name,
						new Label { Text = pin.Place ?? "Dresden", StyleClass = ["Caption"], LineBreakMode = LineBreakMode.TailTruncation }
					}
				};

			Motion.SetFeedback(row, true);

			var tap = new TapGestureRecognizer();

			tap.Tapped += (_, _) => _ = PickAsync(pin);
			row.GestureRecognizers.Add(tap);
			SemanticProperties.SetDescription(row, pin.Title);

			MatchList.Children.Add(row);
		}

		Matches.IsVisible = true;
	}

	private async Task PickAsync(VvoPlanPin pin)
	{
		CloseSearch();

		await Plan.MarkAsync(PlanMark.Focus, pin.Id, pin.Title, PlanFit.Center);
	}

	// ---- toast ----

	private async void ShowToast(string text, bool sticky = false)
	{
		_toast?.Cancel();

		var cancel = new CancellationTokenSource();

		_toast = cancel;
		ToastLabel.Text = text;
		Toast.IsVisible = true;

		if (sticky)
		{
			return;
		}

		try
		{
			await Task.Delay(ToastTime, cancel.Token);

			Toast.IsVisible = false;
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void HideToast()
	{
		_toast?.Cancel();
		Toast.IsVisible = false;
	}
}
