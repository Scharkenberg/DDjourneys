using DDjourneys.Controls;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Core.Providers.Vvo;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>
/// The DVB's plan catalog in the app: the standard line network plan, the GuteNachtLinien plan of the night
/// traffic, the barrier-free line network plan and the MOBI city map (see <see cref="NetworkMapStore"/>),
/// chosen with the chip row at the top - the standard plan is the one the page opens with, a choice of the
/// moment and not a setting. The plan is downloaded on demand, cached for a week, shown from the vector PDF
/// in a <see cref="PlanView"/> (pinch, pan, double tap, wheel, keys and the zoom buttons) with all its detail
/// at every zoom; the JPG at full size only when the PDF cannot be shown (the barrier-free plan names no JPG,
/// so its fallback is the message). The PDF also opens in the system viewer. A document, not a pane page: it
/// is pushed with a plain Shell navigation and popped by back.
/// </summary>
public sealed partial class NetworkMapPage : ContentPage
{
	private readonly NetworkMapStore _maps;
	private CancellationTokenSource? _load;
	private string? _key;

	// Which plan of the catalog the page shows; the refresh and PDF buttons act on it too.
	private VvoNetworkMapKind _kind = VvoNetworkMapKind.Standard;

	// The plan on its way in: until the view answers (shown or failed), a watchdog makes sure the page never
	// stays a silent white sheet - the JPG stands in first, the message with a retry after that.
	private CancellationTokenSource? _await;
	private bool _awaiting;
	private bool _fellBack;

	private static readonly TimeSpan WatchPlan = TimeSpan.FromSeconds(30);

	public NetworkMapPage(NetworkMapStore maps)
	{
		InitializeComponent();

		_maps = maps;

		ShowKind();
	}

	private static ExtrasStrings Strings => LocalizationService.Current.CurrentStrings.Extras;

	protected override void OnAppearing()
	{
		base.OnAppearing();

		if (_key is null)
		{
			Load(refresh: false);
		}
	}

	protected override void OnDisappearing()
	{
		_load?.Cancel();

		base.OnDisappearing();
	}

	// ---- the plan choice ----

	private void StandardTapped(object? sender, TappedEventArgs e) =>
			Switch(VvoNetworkMapKind.Standard);

	private void NightTapped(object? sender, TappedEventArgs e) =>
			Switch(VvoNetworkMapKind.Night);

	private void BarrierFreeTapped(object? sender, TappedEventArgs e) =>
			Switch(VvoNetworkMapKind.BarrierFree);

	private void CityTapped(object? sender, TappedEventArgs e) =>
			Switch(VvoNetworkMapKind.CityMap);

	/// <summary>Shows the chosen plan: the one on screen stays until the new one is in, like a refresh.</summary>
	private void Switch(VvoNetworkMapKind kind)
	{
		if (kind == _kind)
		{
			return;
		}

		_kind = kind;

		// The key belongs to the plan that was loaded; the chosen plan is a fresh load (a same-key refresh
		// would keep the old plan on screen).
		_key = null;

		ShowKind();
		Load(refresh: false);
	}

	/// <summary>The chosen chip says so - the accent fill, and in words the screen reader hears.</summary>
	private void ShowKind()
	{
		Mark(PlanStandard, PlanStandardLabel, _kind == VvoNetworkMapKind.Standard);
		Mark(PlanNight, PlanNightLabel, _kind == VvoNetworkMapKind.Night);
		Mark(PlanBarrierFree, PlanBarrierFreeLabel, _kind == VvoNetworkMapKind.BarrierFree);
		Mark(PlanCityMap, PlanCityMapLabel, _kind == VvoNetworkMapKind.CityMap);
	}

	private static void Mark(Border chip, Label text, bool on)
	{
		Themed.SetIsOn(chip, on);
		Themed.SetIsOn(text, on);
	}

	/// <summary>The plan's own title, the chip row's word for it (the viewer request carries it).</summary>
	private static string PlanTitle(VvoNetworkMapKind kind)
	{
		ExtrasStrings strings = Strings;

		return kind switch
		{
				VvoNetworkMapKind.Night => strings.NetworkMapPlanNight,
				VvoNetworkMapKind.BarrierFree => strings.NetworkMapPlanBarrierFree,
				VvoNetworkMapKind.CityMap => strings.NetworkMapPlanCityMap,
				_ => strings.NetworkMapPlanStandard
		};
	}

	// ---- loading and showing ----

	private void RefreshClicked(object? sender, EventArgs e) =>
			Load(refresh: true);

	private void RetryClicked(object? sender, EventArgs e) =>
			Load(refresh: false);

	private void ZoomInClicked(object? sender, EventArgs e) =>
			_ = Plan.ZoomAsync(1);

	private void ZoomOutClicked(object? sender, EventArgs e) =>
			_ = Plan.ZoomAsync(-1);

	/// <summary>
	/// The plan view was rebuilt (its web view torn down and re-created, e.g. by a window change): the plan it
	/// showed went with the old instance, and the remembered key would keep the reload from showing it again.
	/// </summary>
	private void ReloadPlan(object? sender, EventArgs e)
	{
		_key = null;

		Load(refresh: false);
	}

	private void PlanZoomChanged(object? sender, PlanZoom zoom)
	{
		ZoomInButton.IsEnabled = zoom.CanZoomIn;
		ZoomOutButton.IsEnabled = zoom.CanZoomOut;
	}

	private async void PlanShown(object? sender, bool shown)
	{
		_awaiting = false;
		_await?.Cancel();

		Busy.IsRunning = false;
		Busy.IsVisible = false;

		if (!shown)
		{
			DiagnosticLog.Write("[Network map] the plan could not be drawn");

			// The vector PDF did not draw: the JPG is a coarser plan than none at all.
			if (!_fellBack)
			{
				await FallBackAsync();

				return;
			}

			_key = null;
			Message.IsVisible = true;
			Plan.Opacity = 0;

			return;
		}

		// The plan settles in instead of popping up.
		if (Motion.Enabled)
		{
			await Plan.FadeToAsync(1, 220, Easing.CubicOut);
		}
		else
		{
			Plan.Opacity = 1;
		}
	}

	/// <summary>Shows a plan and keeps watch: a view that says nothing for half a minute counts as failed
	/// (a web view that never loaded leaves the page white - the JPG stands in before the message).</summary>
	private async Task ShowAsync(IPlanTiles tiles)
	{
		_awaiting = true;
		_fellBack = tiles is ImagePlanTiles;

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

		if (!_awaiting
				|| !ReferenceEquals(_await, watch))
		{
			return;
		}

		DiagnosticLog.Write("[Network map] the view stayed silent; showing the JPG instead");

		await FallBackAsync();
	}

	/// <summary>The plan at full size as a picture, when the vector plan did not draw; the message after that.
	/// The barrier-free plan has no JPG - its try is the friendly no-plan error, which lands here as the
	/// message.</summary>
	private async Task FallBackAsync()
	{
		if (_fellBack)
		{
			GiveUp();

			return;
		}

		_fellBack = true;

		try
		{
			NetworkMapFile jpg = await _maps.GetJpgAsync(_kind);

			IPlanTiles tiles = await ImagePlanTiles.OpenAsync(jpg.Path, VvoNetworkMap.PlanName(_kind), CancellationToken.None);

			_key = tiles.Spec.Key;

			await ShowAsync(tiles);
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
		_key = null;
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
			NetworkMapFile pdf = await _maps.GetPdfAsync(_kind);

			await Launcher.Default.OpenAsync(
					new OpenFileRequest(
							PlanTitle(_kind),
							new ReadOnlyFile(pdf.Path, "application/pdf")));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Network map] opening the PDF failed: {ex.Message}");

			Message.IsVisible = _key is null;
		}
		finally
		{
			PdfButton.IsEnabled = true;
		}
	}

	private async void Load(bool refresh)
	{
		_load?.Cancel();

		var cancel = new CancellationTokenSource();

		_load = cancel;

		Message.IsVisible = false;
		RefreshButton.IsEnabled = false;

		if (_key is null)
		{
			Busy.IsRunning = true;
			Busy.IsVisible = true;
		}

		try
		{
			(IPlanTiles tiles, bool fresh) = await OpenAsync(refresh, cancel.Token);

			if (cancel.IsCancellationRequested)
			{
				tiles.Dispose();

				return;
			}

			Stale.IsVisible = !fresh;

			// The same plan again (a refresh that found no new one): what is on screen stays, zoom and all.
			if (string.Equals(tiles.Spec.Key, _key, StringComparison.Ordinal))
			{
				tiles.Dispose();

				return;
			}

			_key = tiles.Spec.Key;

			await ShowAsync(tiles);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			// A friendly message with a way out, never a blank page; a plan on screen stays on screen.
			DiagnosticLog.Write($"[Network map] load failed: {ex.Message}");

			Busy.IsRunning = false;
			Busy.IsVisible = false;
			Message.IsVisible = _key is null;
			Stale.IsVisible = _key is not null;
		}
		finally
		{
			if (ReferenceEquals(_load, cancel))
			{
				RefreshButton.IsEnabled = true;
			}
		}
	}

	/// <summary>The PDF, drawn from its vectors; the JPG at full size when the PDF cannot be had or read.</summary>
	private async Task<(IPlanTiles Tiles, bool Fresh)> OpenAsync(bool refresh, CancellationToken cancellationToken)
	{
		string plan = VvoNetworkMap.PlanName(_kind);

		try
		{
			NetworkMapFile pdf = refresh
					? await _maps.RefreshPdfAsync(_kind, cancellationToken)
					: await _maps.GetPdfAsync(_kind, cancellationToken);

			return (await PdfPlanTiles.OpenAsync(pdf.Path, plan, cancellationToken), pdf.Fresh);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Network map] PDF not shown, trying the JPG: {ex.Message}");
		}

		NetworkMapFile jpg = await _maps.GetJpgAsync(_kind, refresh, cancellationToken);

		return (await ImagePlanTiles.OpenAsync(jpg.Path, plan, cancellationToken), jpg.Fresh);
	}
}
