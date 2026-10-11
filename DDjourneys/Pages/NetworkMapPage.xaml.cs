using DDjourneys.Controls;
using DDjourneys.Core.Diagnostics;
using DDjourneys.Localization;
using DDjourneys.Support;

namespace DDjourneys.Pages;

/// <summary>
/// The DVB line network map: downloaded on demand (see <see cref="NetworkMapStore"/>), cached for a week, shown
/// from the vector PDF in a <see cref="PlanView"/> (pinch, pan, double tap, wheel, keys and the zoom buttons) with
/// all its detail at every zoom; the JPG at full size only when the PDF cannot be shown. The PDF also opens in the
/// system viewer. A document, not a pane page: it is pushed with a plain Shell navigation and popped by back.
/// </summary>
public sealed partial class NetworkMapPage : ContentPage
{
	private const string PlanName = "dvb";

	private readonly NetworkMapStore _maps;
	private CancellationTokenSource? _load;
	private string? _key;

	// The plan on its way in: until the view answers (shown or failed), a watchdog makes sure the page never
	// stays a silent white sheet — the JPG stands in first, the message with a retry after that.
	private CancellationTokenSource? _await;
	private bool _awaiting;
	private bool _fellBack;

	private static readonly TimeSpan WatchPlan = TimeSpan.FromSeconds(30);

	public NetworkMapPage(NetworkMapStore maps)
	{
		InitializeComponent();

		_maps = maps;
	}

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
	/// (a web view that never loaded leaves the page white — the JPG stands in before the message).</summary>
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

	/// <summary>The plan at full size as a picture, when the vector plan did not draw; the message after that.</summary>
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
			NetworkMapFile jpg = await _maps.GetJpgAsync();

			IPlanTiles tiles = await ImagePlanTiles.OpenAsync(jpg.Path, PlanName, CancellationToken.None);

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
			NetworkMapFile pdf = await _maps.GetPdfAsync();

			await Launcher.Default.OpenAsync(
				new OpenFileRequest(
					LocalizationService.Current.CurrentStrings.Extras.NetworkMap,
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
		try
		{
			NetworkMapFile pdf = refresh
				? await _maps.RefreshPdfAsync(cancellationToken)
				: await _maps.GetPdfAsync(cancellationToken);

			return (await PdfPlanTiles.OpenAsync(pdf.Path, PlanName, cancellationToken), pdf.Fresh);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DiagnosticLog.Write($"[Network map] PDF not shown, trying the JPG: {ex.Message}");
		}

		NetworkMapFile jpg = await _maps.GetJpgAsync(refresh, cancellationToken);

		return (await ImagePlanTiles.OpenAsync(jpg.Path, PlanName, cancellationToken), jpg.Fresh);
	}
}
