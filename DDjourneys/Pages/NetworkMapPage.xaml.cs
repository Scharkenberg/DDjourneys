using DDjourneys.Core.Diagnostics;
using DDjourneys.Support;
using SkiaSharp;

namespace DDjourneys.Pages;

/// <summary>
/// The DVB line network map: downloaded on demand (see <see cref="NetworkMapStore"/>), cached for a week,
/// pinch-zoomed and panned (double tap, mouse wheel and the zoom buttons too). The PDF opens in the system
/// viewer. A document, not a pane page: it is pushed with a plain Shell navigation and popped by back.
/// </summary>
public sealed partial class NetworkMapPage : ContentPage
{
	private readonly NetworkMapStore _maps;
	private CancellationTokenSource? _load;
	private double _aspect;

	public NetworkMapPage(NetworkMapStore maps)
	{
		InitializeComponent();

		_maps = maps;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		if (Zoom.Source is null)
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
		_ = Zoom.ZoomStepAsync(zoomIn: true);

	private void ZoomOutClicked(object? sender, EventArgs e) =>
		_ = Zoom.ZoomStepAsync(zoomIn: false);

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

			Message.IsVisible = Zoom.Source is null;
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
		Stale.IsVisible = false;
		Busy.IsRunning = true;
		Busy.IsVisible = true;
		RefreshButton.IsEnabled = false;

		try
		{
			NetworkMapFile map = refresh
				? await _maps.RefreshAsync(cancel.Token)
				: await _maps.GetJpgAsync(cancel.Token);

			if (cancel.IsCancellationRequested)
			{
				return;
			}

			// The aspect lets the zoom clamp to the paper's edge, not the letterboxed frame. The header of the
			// file tells it: the picture itself (several megapixels) is not decoded for two numbers.
			_aspect = await Task.Run(() => AspectOf(map.Path), cancel.Token);

			Zoom.ContentAspect = _aspect;
			Zoom.Reset();
			Zoom.Source = ImageSource.FromFile(map.Path);
			Stale.IsVisible = !map.Fresh;

			// The plan settles in instead of popping up.
			if (Motion.Enabled)
			{
				await Zoom.FadeToAsync(1, 220, Easing.CubicOut);
			}
			else
			{
				Zoom.Opacity = 1;
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			// A friendly message with a way out, never a blank page; a plan on screen stays on screen.
			DiagnosticLog.Write($"[Network map] load failed: {ex.Message}");

			Message.IsVisible = Zoom.Source is null;
			Stale.IsVisible = Zoom.Source is not null;
		}
		finally
		{
			if (ReferenceEquals(_load, cancel))
			{
				Busy.IsRunning = false;
				Busy.IsVisible = false;
				RefreshButton.IsEnabled = true;
			}
		}
	}

	private static double AspectOf(string path)
	{
		using SKCodec? codec = SKCodec.Create(path);

		return codec is { Info: { Height: > 0 } info }
			? (double)info.Width / info.Height
			: 0;
	}
}
