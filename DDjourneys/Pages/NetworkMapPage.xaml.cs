using DDjourneys.Controls;
using DDjourneys.Core.Diagnostics;
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

	private void PlanZoomChanged(object? sender, PlanZoom zoom)
	{
		ZoomInButton.IsEnabled = zoom.CanZoomIn;
		ZoomOutButton.IsEnabled = zoom.CanZoomOut;
	}

	private async void PlanShown(object? sender, bool shown)
	{
		Busy.IsRunning = false;
		Busy.IsVisible = false;

		if (!shown)
		{
			DiagnosticLog.Write("[Network map] the plan could not be drawn");

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

	private async void OpenPdfClicked(object? sender, EventArgs e)
	{
		PdfButton.IsEnabled = false;

		try
		{
			NetworkMapFile pdf = await _maps.GetPdfAsync();

			await Launcher.Default.OpenAsync(
				new OpenFileRequest("Liniennetzplan", new ReadOnlyFile(pdf.Path, "application/pdf")));
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

			await Plan.ShowAsync(tiles);
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
